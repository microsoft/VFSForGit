#pragma once

#include <sys/kernel_types.h>
#include <sys/_types/_fsid_t.h>
#include <kern/assert.h>
#include "kernel-header-wrappers/vnode.h"
#include "public/FsidInode.h"
#include "KextLog.hpp"

struct SizeOrError
{
    size_t size;
    errno_t error;
};

SizeOrError Vnode_ReadXattr(vnode_t _Nonnull vnode, const char* _Nonnull xattrName, void* _Nullable buffer, size_t bufferSize);
FsidInode Vnode_GetFsidAndInode(vnode_t _Nonnull vnode, vfs_context_t _Nonnull context);

template <typename T> struct remove_reference
{
    typedef T type;
};
template <typename T> struct remove_reference<T&>
{
    typedef T type;
};
template <typename T> struct remove_reference<T&&>
{
    typedef T type;
};

template <class T>
    constexpr typename remove_reference<T>::type&& move(T&& t) noexcept
{
    return static_cast<typename remove_reference<T>::type&&>(t);
}

class VnodeIORef
{
    _Nullable vnode_t vnode;
    
public:
    // Transfers ownership if not nullptr
    explicit VnodeIORef(_Nullable vnode_t&& ref);
    explicit VnodeIORef(_Nullable vnode_t& ref);
    VnodeIORef();
    VnodeIORef(VnodeIORef&& rval);
    VnodeIORef(const VnodeIORef& rhs);
    VnodeIORef& operator=(const VnodeIORef& rhs);
    VnodeIORef& operator=(VnodeIORef&& rhs);
    ~VnodeIORef();
    
    _Nullable vnode_t Get() const;
    bool IsNull() const;
    void Transfer(_Nullable vnode_t& newVnode);
    void Reset();
    void SetAndRetain(_Nullable vnode_t newVnode);
};

inline bool vnode_isdir(const VnodeIORef& vnodeRef)
{
    return vnode_isdir(vnodeRef.Get());
}

inline uint32_t vnode_vid(const VnodeIORef& vnodeRef)
{
    return vnode_vid(vnodeRef.Get());
}

template <typename... args>
    void KextLog_PrintfVnodePathAndProperties(KextLog_Level loglevel, const VnodeIORef& vnode, const char* _Nonnull fmt, args... a)
{
    KextLog_PrintfVnodePathAndProperties(loglevel, vnode.Get(), fmt, a...);
}

template <typename... args>
    void KextLog_PrintfVnodeProperties(KextLog_Level loglevel, const VnodeIORef& vnode, const char* _Nonnull fmt, args... a)
{
    KextLog_PrintfVnodeProperties(loglevel, vnode.Get(), fmt, a...);
}

inline VnodeIORef::VnodeIORef(_Nullable vnode_t&& ref) :
    vnode(ref)
{
}

inline VnodeIORef::VnodeIORef(_Nullable vnode_t& ref) :
    vnode(ref)
{
    ref = NULLVP;
}

inline VnodeIORef::VnodeIORef() :
    vnode(NULLVP)
{
}

inline VnodeIORef::VnodeIORef(VnodeIORef&& rval) :
    vnode(rval.vnode)
{
    rval.vnode = NULLVP;
}

inline VnodeIORef::VnodeIORef(const VnodeIORef& rhs) :
    vnode(rhs.vnode)
{
    errno_t error = vnode_get(rhs.vnode);
    assert(error == 0); // rhs should already hold an iocount reference, so vnode_get should always succeed
    (void)error;
}

inline VnodeIORef& VnodeIORef::operator=(const VnodeIORef& rhs)
{
    vnode_t newVnode = rhs.vnode;
    if (this->vnode == newVnode)
    {
        return *this;
    }
    
    if (this->vnode != NULLVP)
    {
        vnode_put(this->vnode);
    }
    
    this->vnode = newVnode;
    if (newVnode != NULLVP)
    {
        errno_t error = vnode_get(newVnode);
        assert(error == 0); // rhs should already hold an iocount reference, so vnode_get should always succeed
        (void)error;
    }
    
    return *this;
}

inline VnodeIORef& VnodeIORef::operator=(VnodeIORef&& rhs)
{
    if (this->vnode != NULLVP)
    {
        vnode_put(this->vnode);
    }

    this->vnode = rhs.vnode;
    rhs.vnode = NULLVP;
    return *this;
}

inline VnodeIORef::~VnodeIORef()
{
    this->Reset();
}

inline _Nullable vnode_t VnodeIORef::Get() const
{
    return this->vnode;
}

inline bool VnodeIORef::IsNull() const
{
    return this->vnode == nullptr;
}

inline void VnodeIORef::Transfer(_Nullable vnode_t& newVnode)
{
    if (this->vnode != NULLVP)
    {
        vnode_put(this->vnode);
    }
    
    this->vnode = newVnode;
    newVnode = NULLVP;
}

inline void VnodeIORef::Reset()
{
    if (this->vnode != NULLVP)
    {
        vnode_put(this->vnode);
        this->vnode = NULLVP;
    }
}

inline void VnodeIORef::SetAndRetain(_Nullable vnode_t newVnode)
{
    if (this->vnode == newVnode)
    {
        return;
    }
    
    if (newVnode != nullptr)
    {
        errno_t error = vnode_get(newVnode);
        assert(error == 0); // rhs should already hold an iocount reference, so vnode_get should always succeed
        (void)error;
    }
    
    this->vnode = newVnode;
}
