#pragma once

#include "../PrjFSKext/kernel-header-wrappers/vnode.h"
#include "../PrjFSKext/kernel-header-wrappers/mount.h"
#include "../PrjFSKext/public/FsidInode.h"
#include <memory>
#include <string>

// The struct names mount and vnode are dictated by mount_t and vnode_t's
// definitions as (opaque/forward declared) pointers to those structs.
// As the (testable) kext treats them as entirely opaque, we can implement
// them as we wish for purposes of testing.

struct mount
{
private:
    vfsstatfs statfs;
    uint64_t nextInode;
    
public:
    static std::shared_ptr<mount> Create(const char* fileSystemTypeName, fsid_t fsid, uint64_t initialInode);
    
    inline fsid_t GetFsid() const { return this->statfs.f_fsid; }
    
    friend struct vnode;
    friend vfsstatfs* vfs_statfs(mount_t mountPoint);
    friend FsidInode Vnode_GetFsidAndInode(vnode_t vnode, vfs_context_t vfsContext, bool useLinkIDForInode);
};

struct vnode
{
private:
    std::weak_ptr<vnode> weakSelfPointer;
    std::shared_ptr<mount> mountPoint;
    
    uint64_t inode;
    uint32_t vid;
    int32_t ioCount = 0;
    bool isRecycling = false;
    
    errno_t getPathError = 0;
    
    vtype type = VREG;
    
    std::string path;
    const char* name;
    
    void SetPath(const std::string& path);

    explicit vnode(const std::shared_ptr<mount>& mount);
    
    vnode(const vnode&) = delete;
    vnode& operator=(const vnode&) = delete;
    
public:
    static std::shared_ptr<vnode> Create(const std::shared_ptr<mount>& mount, const char* path, vtype vnodeType = VREG);
    static std::shared_ptr<vnode> Create(const std::shared_ptr<mount>& mount, const char* path, vtype vnodeType, uint64_t inode);
    ~vnode();
    
    uint64_t GetInode() const { return this->inode; }
    uint32_t GetVid() const { return this->vid; }
    void SetGetPathError(errno_t error);
    void StartRecycling();

    friend int vnode_isrecycled(vnode_t vnode);
    friend uint32_t vnode_vid(vnode_t vnode);
    friend const char* vnode_getname(vnode_t vnode);
    friend vtype vnode_vtype(vnode_t vnode);
    friend mount_t vnode_mount(vnode_t vnode);
    friend int vnode_get(vnode_t vnode);
    friend int vnode_put(vnode_t vnode);
    friend errno_t vnode_lookup(const char* path, int flags, vnode_t* foundVnode, vfs_context_t vfsContext);
    friend int vn_getpath(vnode_t vnode, char* pathBuffer, int* pathLengthInOut);
    friend FsidInode Vnode_GetFsidAndInode(vnode_t vnode, vfs_context_t vfsContext, bool useLinkIDForInode);
};


void MockVnodes_CheckAndClear();

