#pragma once

#include "../PrjFSKext/kernel-header-wrappers/vnode.h"
#include "../PrjFSKext/kernel-header-wrappers/mount.h"
#include "../PrjFSKext/public/FsidInode.h"
#include <memory>
#include <string>
#include <vector>
#include <unordered_map>

// An aggregate for setting properties of newly created mock vnodes with sensible defaults.
struct VnodeCreationProperties
{
    vtype type = VREG;
    // UINT64_MAX is a special value that causes the associated mount point to
    // automatically assign an inode. Set this field to something else to
    // explicitly choose an inode number.
    uint64_t inode = UINT64_MAX;
    std::shared_ptr<vnode> parent;
};

// The struct names mount and vnode are dictated by mount_t and vnode_t's
// definitions as (opaque/forward declared) pointers to those structs.
// As the (testable) kext treats them as entirely opaque, we can implement
// them as we wish for purposes of testing.

struct mount
{
private:
    vfsstatfs statfs;
    uint64_t nextInode;
    std::weak_ptr<mount> weakSelfPointer;
    std::weak_ptr<vnode> rootVnode;

public:
    static std::shared_ptr<mount> Create(const char* fileSystemTypeName = "hfs", fsid_t fsid = fsid_t{}, uint64_t initialInode = 0);

    std::shared_ptr<vnode> CreateVnodeTree(const std::string& path, vtype vnodeType = VREG);
    // By default, CreateVnode() will create a regular file with an
    // auto-assigned inode and no existing parent vnode.
    // See struct VnodeCreationProperties for changing this.
    std::shared_ptr<vnode> CreateVnode(std::string path, VnodeCreationProperties properties = VnodeCreationProperties{});
    
    fsid_t GetFsid() const { return this->statfs.f_fsid; }
    std::shared_ptr<vnode> GetRootVnode() const { return this->rootVnode.lock(); }
    
    friend struct vnode;
    friend vfsstatfs* vfs_statfs(mount_t mountPoint);
};

// On the subject of mock vnode lifetimes and memory management:
//
// To create a standalone mock vnode, use mount::CreateVnode(), if you need a
// full directory hierarchy, use mount::CreateVnodeTree().
// Currently, vnode::Create() also exists and works, but it will soon be
// replaced by an equivalent mount::CreateVnode() overload.
// Don't attempt to create vnodes directly via operator new/constructors.
//
// Tests are expected to manage the lifetimes of mock vnodes they create by
// maintaining shared_ptr<vnode> references. When all of these references go
// out of scope at the end of the test, this should cause all mock vnodes to
// be destroyed, so an extra sign of successful test execution is that there are
// no live vnodes left after the test. (Call MockVnodes_CheckAndClear() in
// tearDown)
//
// This is possible because vnodes will internally retain strong references to
// their registered parent vnode, but not the other way around, so there are no
// circular references. Similarly, and for the same reason, mock mount points do
// not hold strong references to mock vnodes, not even their root vnode, but
// vnodes hold strong references to their mount point.
//
// This way, we can have a mock mount point as a test class instance variable
// and don't have to manually clear out the rootVnode after every test.
//
struct VnodeMockErrors
{
    errno_t getpath = 0;
    errno_t getattr = 0;
};

struct vnode
{
private:
    std::weak_ptr<vnode> weakSelfPointer;
    std::shared_ptr<mount> mountPoint;
    std::shared_ptr<vnode> parent;

public:
    typedef std::unordered_map<std::string, std::vector<uint8_t>> XattrMap;
    XattrMap xattrs;

private:
    bool isRecycling = false;
    vtype type = VREG;
    uint64_t inode;
    uint32_t vid;
    int32_t ioCount = 0;
    
    std::string path;
    const char* name;
    
    void SetAndRegisterPath(const std::string& path);

    explicit vnode(const std::shared_ptr<mount>& mount);
    explicit vnode(const std::shared_ptr<mount>& mount, VnodeCreationProperties properties);

    vnode(const vnode&) = delete;
    vnode& operator=(const vnode&) = delete;
    
public:
    static std::shared_ptr<vnode> Create(const std::shared_ptr<mount>& mount, const char* path, vtype vnodeType = VREG);
    ~vnode();

    VnodeMockErrors errors;
    vnode_attr attrValues;
    
    uint64_t GetInode() const          { return this->inode; }
    uint32_t GetVid() const            { return this->vid; }
    const char* GetName() const        { return this->name; }
    mount_t GetMountPoint() const      { return this->mountPoint.get(); }
    bool IsRecycling() const           { return this->isRecycling; }
    vtype GetVnodeType() const         { return this->type; }
    std::shared_ptr<vnode> const GetParentVnode() { return this->parent; }
    
    struct BytesOrError
    {
        errno_t error;
        std::vector<uint8_t> bytes;
    };
    
    BytesOrError ReadXattr(const char* xattrName);

    void StartRecycling();

    errno_t RetainIOCount();
    void ReleaseIOCount();

    friend struct mount;
    friend int vnode_getattr(vnode_t vp, struct vnode_attr* vap, vfs_context_t ctx);
    friend int vn_getpath(vnode_t vnode, char* pathBuffer, int* pathLengthInOut);
    friend int vnode_isnamedstream(vnode_t vp);
};


void MockVnodes_CheckAndClear();

