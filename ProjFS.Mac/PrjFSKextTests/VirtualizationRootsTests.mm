#include "../PrjFSKext/VirtualizationRoots.hpp"
#include "../PrjFSKext/kernel-header-wrappers/mount.h"
#import <XCTest/XCTest.h>
#include "../PrjFSKext/Memory.hpp"
#include "../PrjFSKext/Locks.hpp"
#include "../PrjFSKext/VnodeUtilities.hpp"
#include "../PrjFSKext/KextLog.hpp"
#include <unordered_map>
#include <unordered_set>
#include <set>
#include <memory>
#include <string>

using std::string;
using std::make_pair;

typedef std::shared_ptr<mount> MountPointer;

typedef std::shared_ptr<vnode> VnodePointer;
typedef std::weak_ptr<vnode> VnodeWeakPointer;
typedef std::unordered_map<string, VnodeWeakPointer> PathToVnodeMap;
typedef std::unordered_map<vnode_t, VnodeWeakPointer> WeakVnodeMap;

class PrjFSProviderUserClient
{
};

struct mount
{
private:
    vfsstatfs statfs;
    
public:
    static MountPointer WithName(const char* fileSystemName);
    
    friend vfsstatfs* vfs_statfs(mount_t mountPoint);
};

struct vnode
{
private:
    MountPointer mountPoint;
    VnodeWeakPointer weakSelfPointer;

    int32_t ioCount = 0;
    bool isRecycling = false;
    
    vtype type = VREG;
    
    string path;
    const char* name;
    
    void SetPath(const string& path);

    explicit vnode(const MountPointer& mount);
    
    vnode(const vnode&) = delete;
    vnode& operator=(const vnode&) = delete;
    
public:
    static VnodePointer WithPath(const MountPointer& mount, const char* path);
    ~vnode();

    friend int vnode_isrecycled(vnode_t vnode);
    friend const char* vnode_getname(vnode_t vnode);
    friend vtype vnode_vtype(vnode_t vnode);
    friend mount_t vnode_mount(vnode_t vnode);
    friend int vnode_put(vnode_t vnode);
    friend errno_t vnode_lookup(const char* path, int flags, vnode_t* foundVnode, vfs_context_t vfsContext);
};

static PathToVnodeMap s_vnodesByPath;
static WeakVnodeMap s_allVnodes;

MountPointer mount::WithName(const char* fileSystemName)
{
    MountPointer result(new mount{});
    assert(strlen(fileSystemName) + 1 < sizeof(result->statfs.f_fstypename));
    strlcpy(result->statfs.f_fstypename, fileSystemName, sizeof(result->statfs.f_fstypename));
    return result;
}


vnode::vnode(const MountPointer& mount) :
    mountPoint(mount),
    name(nullptr)
{
}

vnode::~vnode()
{
    assert(this->ioCount == 0);
}

VnodePointer vnode::WithPath(const MountPointer& mount, const char* path)
{
    VnodePointer result(new vnode(mount));
    s_allVnodes.insert(make_pair(result.get(), VnodeWeakPointer(result)));
    result->weakSelfPointer = result;
    result->SetPath(path);
    return result;
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
    
    s_vnodesByPath.insert(make_pair(path, this->weakSelfPointer));
}

int vnode_isrecycled(vnode_t vnode)
{
    return vnode->isRecycling;
}

const char* vnode_getname(vnode_t vnode)
{
    return vnode->name;
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
    return 0;
}

FsidInode Vnode_GetFsidAndInode(vnode_t vnode, vfs_context_t vfsContext)
{
    return FsidInode{};
}

errno_t vnode_lookup(const char* path, int flags, vnode_t* foundVnode, vfs_context_t vfsContext)
{
    PathToVnodeMap::const_iterator found = s_vnodesByPath.find(path);
    if (found == s_vnodesByPath.end())
    {
        return ENOENT;
    }
    else if (VnodePointer vnode = found->second.lock())
    {
        // vnode_lookup returns a vnode with an iocount
        ++vnode->ioCount;
        *foundVnode = vnode.get();
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
    return 0;
}

vtype vnode_vtype(vnode_t vnode)
{
    return vnode->type;
}

mount_t vnode_mount(vnode_t vnode)
{
    return vnode->mountPoint.get();
}

int vnode_put(vnode_t vnode)
{
    assert(vnode->ioCount > 0);
    --vnode->ioCount;
    return 0;
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
}




void Memory_Free(void* memory, uint32_t sizeBytes)
{
    free(memory);
}

void* Memory_Alloc(uint32_t sizeBytes)
{
    return malloc(sizeBytes);
}


void RWLock_AcquireExclusive(RWLock& lock)
{
}

void RWLock_ReleaseExclusive(RWLock& lock)
{
}


const void* KextLog_Unslide(const void* pointer)
{
    return pointer;
}

void KextLog_Printf(KextLog_Level level, const char* format, ...)
{
}

@interface VirtualizationRootsTests : XCTestCase

@end

@implementation VirtualizationRootsTests
{
    PrjFSProviderUserClient dummyClient;
    pid_t dummyClientPid;
    MountPointer testMountPoint;
}

- (void)setUp
{
    self->dummyClientPid = 100;
    testMountPoint = mount::WithName("hfs");
}

- (void)tearDown
{
    for (WeakVnodeMap::const_iterator cur = s_allVnodes.begin(); cur != s_allVnodes.end(); ++cur)
    {
        VnodePointer strong = cur->second.lock();
        XCTAssertFalse(strong);
    }
    
    s_allVnodes.clear();
}


- (void)testRegisterProviderForPath_NonexistentPath
{
    const char* path = "/Users/test/code/Does/Not/Exist";
    VirtualizationRootResult result = VirtualizationRoot_RegisterProviderForPath(&self->dummyClient, self->dummyClientPid, path);
    XCTAssertEqual(result.error, ENOENT);
    XCTAssertFalse(VirtualizationRoot_IsValidRootHandle(result.root));
}

- (void)testRegisterProviderForPath_NonDirectoryPath
{
    const char* path = "/Users/test/code/NotADirectory.cpp";
    
    VnodePointer vnode = vnode::WithPath(self->testMountPoint, path);
    
    VirtualizationRootResult result = VirtualizationRoot_RegisterProviderForPath(&self->dummyClient, self->dummyClientPid, path);
    XCTAssertEqual(result.error, ENOTDIR);
    XCTAssertFalse(VirtualizationRoot_IsValidRootHandle(result.root));
}

@end
