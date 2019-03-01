#include "MockVnodeAndMount.hpp"
#include "../PrjFSKext/VnodeUtilities.hpp"
#include "KextMockUtilities.hpp"
#include <unordered_map>
#include <sys/errno.h>

using std::string;
using std::unordered_map;
using std::shared_ptr;
using std::weak_ptr;

typedef unordered_map<string, weak_ptr<vnode>> PathToVnodeMap;
typedef unordered_map<vnode_t, weak_ptr<vnode>> WeakVnodeMap;

static PathToVnodeMap s_vnodesByPath;
static WeakVnodeMap s_allVnodes;

shared_ptr<mount> mount::Create(const char* fileSystemTypeName, fsid_t fsid, uint64_t initialInode)
{
    shared_ptr<mount> result(new mount{});
    assert(strlen(fileSystemTypeName) + 1 < sizeof(result->statfs.f_fstypename));
    result->statfs.f_fsid = fsid;
    result->nextInode = initialInode;
    strlcpy(result->statfs.f_fstypename, fileSystemTypeName, sizeof(result->statfs.f_fstypename));
    return result;
}


vnode::vnode(const shared_ptr<mount>& mount) :
    mountPoint(mount),
    name(nullptr),
    inode(mount->nextInode++)
{
}

vnode::~vnode()
{
    assert(this->ioCount == 0);
}

shared_ptr<vnode> vnode::Create(const shared_ptr<mount>& mount, const char* path, vtype vnodeType)
{
    shared_ptr<vnode> result(new vnode(mount));
    s_allVnodes.insert(make_pair(result.get(), weak_ptr<vnode>(result)));
    result->weakSelfPointer = result;
    result->SetPath(path);
    result->type = vnodeType;
    return result;
}

shared_ptr<vnode> vnode::Create(const shared_ptr<mount>& mount, const char* path, vtype vnodeType, uint64_t inode)
{
    shared_ptr<vnode> result = Create(mount, path, vnodeType);
    result->inode = inode;
    return result;
}

void vnode::StartRecycling()
{
    s_vnodesByPath.erase(this->path);
    this->path.clear();
    this->type = VBAD;
    this->vid++;
    this->isRecycling = true;
}

void vnode::SetGetPathError(errno_t error)
{
    this->getPathError = error;
}

void vnode::SetPath(const string& path)
{
    s_vnodesByPath.erase(this->path);

    this->path = path;
    size_t lastSlash = this->path.rfind('/');
    if (lastSlash == string::npos)
    {
        this->name = this->path.c_str();
    }
    else
    {
        this->name = this->path.c_str() + lastSlash + 1;
    }
    
    // Any existing vnodes for that path should be destroyed/recycled by now
    assert(!s_vnodesByPath[path].lock());
    s_vnodesByPath[path] = this->weakSelfPointer;
}

int vnode_isrecycled(vnode_t vnode)
{
    return vnode->IsRecycling();
}

const char* vnode_getname(vnode_t vnode)
{
    return vnode->GetName();
}

void vnode_putname(const char* name)
{
    // TODO: track name reference counts
}

int vnode_isdir(vnode_t vnode)
{
    return vnode_vtype(vnode) == VDIR;
}

int vn_getpath(vnode_t vnode, char* pathBuffer, int* pathLengthInOut)
{
    assert(*pathLengthInOut >= MAXPATHLEN);
    if (vnode->getPathError != 0)
    {
        return vnode->getPathError;
    }
    else if (vnode->path.empty() || vnode->isRecycling)
    {
        // TODO: check what the real vn_getpath() would return here
        return EIO;
    }
    else
    {
        strlcpy(pathBuffer, vnode->path.c_str(), MIN(*pathLengthInOut, MAXPATHLEN));
        return 0;
    }
}

// TODO: Perhaps switch to the real version of this function and mock the KPIs it uses
FsidInode Vnode_GetFsidAndInode(vnode_t vnode, vfs_context_t vfsContext, bool useLinkIDForInode)
{
    // TODO: extend vnode mock to distinguish betweeen fileid and linkid?
    return FsidInode{ vnode->GetMountPoint()->GetFsid(), vnode->GetInode() };
}

errno_t vnode_lookup(const char* path, int flags, vnode_t* foundVnode, vfs_context_t vfsContext)
{
    PathToVnodeMap::const_iterator found = s_vnodesByPath.find(path);
    if (found == s_vnodesByPath.end())
    {
        return ENOENT;
    }
    else if (shared_ptr<vnode> vnode = found->second.lock())
    {
        // vnode_lookup returns a vnode with an iocount
        errno_t error = vnode->RetainIOCount();
        if (error == 0)
        {
            *foundVnode = vnode.get();
        }
        
        return 0;
    }
    else
    {
        s_vnodesByPath.erase(found);
        return ENOENT;
    }
}

uint32_t vnode_vid(vnode_t vnode)
{
    return vnode->GetVid();
}

vtype vnode_vtype(vnode_t vnode)
{
    return vnode->GetVnodeType();
}

mount_t vnode_mount(vnode_t vnode)
{
    return vnode->GetMountPoint();
}

void vnode::ReleaseIOCount()
{
    assert(this->ioCount > 0);
    --this->ioCount;
}

int vnode_put(vnode_t vnode)
{
    vnode->ReleaseIOCount();
    return 0;
}

int vnode_get(vnode_t vnode)
{
    return vnode->RetainIOCount();
}

errno_t vnode::RetainIOCount()
{
    if (this->ioCount > 0 || !this->isRecycling)
    {
        ++this->ioCount;
        return 0;
    }
    else
    {
        // TODO: check what real vnode_get returns for recycled vnodes
        return EBADF;
    }
}


vfsstatfs* vfs_statfs(mount_t mountPoint)
{
    return &mountPoint->statfs;
}

vfs_context_t vfs_context_create(vfs_context_t contextToClone)
{
    return nullptr;
}

int vfs_context_rele(vfs_context_t vfsContext)
{
    return 0;
}


void vfs_setauthcache_ttl(mount_t mountPoint, int ttl)
{
    MockCalls::RecordFunctionCall(vfs_setauthcache_ttl, mountPoint, ttl);
}

void MockVnodes_CheckAndClear()
{
    // All of the vnodes in s_allVnodes should have been destroyed by the time MockVnodes_CheckAndClear is called
    for (WeakVnodeMap::const_iterator cur = s_allVnodes.begin(); cur != s_allVnodes.end(); ++cur)
    {
        shared_ptr<vnode> strong = cur->second.lock();
        assert(!strong);
    }
    
    s_allVnodes.clear();
}
