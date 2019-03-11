#include "VnodeUtilities.hpp"
#include "KextLog.hpp"
#include "kernel-header-wrappers/vnode.h"
#include "kernel-header-wrappers/mount.h"
#include "PerformanceTracing.hpp"
#include "public/Message.h"
#include "VirtualizationRoots.hpp"
#include "ProviderMessaging.hpp"

#ifdef KEXT_UNIT_TESTING
#include "VnodeUtilitiesTestable.hpp"
#endif

#include <sys/xattr.h>
#include <sys/kauth.h>
#include <string.h>
#include <libkern/libkern.h>
#include <kern/assert.h>

extern "C" int mac_vnop_getxattr(struct vnode *, const char *, char *, size_t, size_t *);

#ifndef KEXT_UNIT_TESTING

FsidInode Vnode_GetFsidAndInode(vnode_t vnode, vfs_context_t context, bool useLinkIDForInode)
{
    vnode_attr attrs;
    VATTR_INIT(&attrs);
    // va_linkid is unique per hard link on HFS+, va_fileid identifies the link target. Always equal on APFS, no way to distinguish between links.
    VATTR_WANTED(&attrs, va_linkid);

    int errno = vnode_getattr(vnode, &attrs, context);
    if (0 != errno)
    {
        KextLog_FileError(vnode, "Vnode_GetFsidAndInode: vnode_getattr failed with errno %d", errno);
    }
    
    vfsstatfs* statfs = vfs_statfs(vnode_mount(vnode));
    return { statfs->f_fsid, attrs.va_linkid };
}

SizeOrError Vnode_ReadXattr(vnode_t vnode, const char* xattrName, void* buffer, size_t bufferSize)
{
    size_t actualSize = bufferSize;
    errno_t error = mac_vnop_getxattr(vnode, xattrName, static_cast<char*>(buffer), bufferSize, &actualSize);
    return SizeOrError { actualSize, error };
}

#endif

static SizeOrError GetVnodePath(vnode_t currentVnode, char(&vnodePathBuffer)[PrjFSMaxPath], vfs_context_t context, PerfTracer& perfTracer, bool forceUsingProviderIfPossible)
{
    int vnodePathLength = PrjFSMaxPath;
    PerfSample pathSample(&perfTracer, PrjFSPerfCounter_VnodeGetPath);
    
    errno_t error = forceUsingProviderIfPossible ? EIO : vn_getpath(currentVnode, vnodePathBuffer, &vnodePathLength);
    if (0 == error)
    {
        return SizeOrError{ .error = error };
    }

    if (!forceUsingProviderIfPossible)
    {
        KextLog_ErrorVnodeProperties(currentVnode, "GetVnodePath (%p): vn_getpath failed, error = %d", KextLog_Unslide(currentVnode), error);
    }

    FsidInode vnodeFsidInode = Vnode_GetFsidAndInode(currentVnode, context, true /* use link ID where available to increase chances of path we're expecting */);
    KextLog_Info("HandleVnodeOperation: Requesting path for vnode (%p) with fsid 0x%x:0x%x, inode %llu from provider", KextLog_Unslide(currentVnode), vnodeFsidInode.fsid.val[0], vnodeFsidInode.fsid.val[1], vnodeFsidInode.inode);
    size_t vnodePathSize = 0;
    int result = KAUTH_RESULT_ALLOW;
    if (!ProviderMessaging_TrySendRequestAndWaitForResponse(
        RootHandle_AnyActiveProvider, MessageType_KtoU_RequestVnodePath, currentVnode, vnodeFsidInode, nullptr /* path not known */, 0 /* pid */, "mach_kernel", &result, &error, vnodePathBuffer, sizeof(vnodePathBuffer), &vnodePathSize))
    {
        KextLog_Error("HandleVnodeOperation: getting vnode (%p) path from user space also failed, error = %d", KextLog_Unslide(currentVnode), error);
        // Retry just in case, especially if forceUsingProviderIfPossible
        error = vn_getpath(currentVnode, vnodePathBuffer, &vnodePathLength);
        return SizeOrError{ .error = error, .size = (error == 0) ? vnodePathLength : 0u };
    }
    else
    {
        vnodePathLength = static_cast<int>(strnlen(vnodePathBuffer, sizeof(vnodePathBuffer)));
        if (vnodePathLength != vnodePathSize - 1)
        {
            KextLog_Error("HandleVnodeOperation: getting vnode path from user space succeeded, but got unexpected string length: %d, returned data size: %lu", vnodePathLength, vnodePathSize);
        }
        KextLog_Info("HandleVnodeOperation: Getting vnode (%p) path from user space succeeded: '%.*s'", KextLog_Unslide(currentVnode), vnodePathLength, vnodePathBuffer);
        
        vnode_t returnedPathVnode = NULLVP;
        error = vnode_lookup(vnodePathBuffer, 0 /*flags*/, &returnedPathVnode, context);
        if (error == 0 && returnedPathVnode != currentVnode)
        {
            FsidInode lookedUpInode = Vnode_GetFsidAndInode(returnedPathVnode, context, true);
            KextLog_Error("Mismatch between original vnode (%p) and lookup of provider-supplied path (%p). Inode of original vnode: 0x%llx, lookup result: 0x%llx",
                KextLog_Unslide(currentVnode), KextLog_Unslide(returnedPathVnode), vnodeFsidInode.inode, lookedUpInode.inode);
        }
        else if (error != 0)
        {
            KextLog_Error("vnode_lookup returned %d for lookup of provider-supplied path of '.*%s'", vnodePathLength, vnodePathBuffer);
        }
        
        if (returnedPathVnode != nullptr)
        {
            vnode_put(returnedPathVnode);
        }
    }
    
    return SizeOrError { .size = static_cast<size_t>(vnodePathLength) };
}

KEXT_STATIC void TruncatePathToParent(char* path, size_t pathLength)
{
    assertf(pathLength > 1, "ParentPath: path should not be empty or /. Got length %lu, path '%s'", pathLength, path);
    
    // Eliminate any trailing slash
    if (path[pathLength - 1] == '/')
    {
        path[pathLength - 1] = '\0';
        --pathLength;
    }
    
    char* lastSlash = path + pathLength - 1;
    while (lastSlash != path)
    {
        if (*lastSlash == '/')
        {
            // Found last path element, truncate path before it
            *lastSlash = '\0';
            return;
        }
        --lastSlash;
    }
}

vnode_t Vnode_GetParentViaProvider(vnode_t vnode, vfs_context_t context, PerfTracer& perfTracer)
{
    vnode_t parent = vnode_getparent(vnode);
#ifdef KEXT_UNIT_TESTING
    const bool failureInjection = false; // unit tests can explicitly inject errors
#else
    bool failureInjection = (random() & 0x7f) == 0; // deliberately fail 1 in 128
#endif
    if (parent != NULLVP && failureInjection)
    {
        KextLog_File(vnode, "Vnode_GetParentViaProvider: Simulating vnode_getparent failure (%p)", KextLog_Unslide(vnode));
        vnode_put(parent);
    }
    else if (parent != NULLVP || vnode_isvroot(vnode))
    {
        return parent;
    }
    
    // Strategy to follow:
    //  * Try to get the vnode's path, via detour to provider if necessary
    //  * Try vnode_getparent() again, as that should have refreshed the name cache
    //  * If that fails, explicitly manipulate the path to go one level higher and use vnode_lookup()
    //  * Assuming that worked, check this now agrees with vnode_getparent()
    char path[PrjFSMaxPath] = "";
    SizeOrError pathLength = GetVnodePath(vnode, path, context, perfTracer, failureInjection);
    
    parent = vnode_getparent(vnode);
    if (parent != NULLVP || pathLength.error != 0)
    {
        return parent;
    }

    TruncatePathToParent(path, pathLength.size);
    
    vnode_t parentFromPath = NULLVP;
    errno_t error = vnode_lookup(path, 0 /* flags */, &parentFromPath, context);
    if (error == 0)
    {
        parent = vnode_getparent(vnode);
        if (parent == NULLVP)
        {
            KextLog_FileError(vnode, "Vnode_GetParentViaProvider: vnode_getparent(%p) still failed after successfully getting parent via path (%s)", KextLog_Unslide(vnode), path);
            parent = parentFromPath;
        }
        else if (parent != parentFromPath)
        {
            KextLog_FileError(parent, "Vnode_GetParentViaProvider: Mismatch between vnode_getparent(%p) and result of getting parent via path (%s)", KextLog_Unslide(vnode), path);
            vnode_put(parentFromPath);
        }
        else
        {
            vnode_put(parentFromPath);
        }
        
        return parent;
    }
    else
    {
        KextLog_FileError(vnode, "Vnode_GetParentViaProvider (%p): vnode_lookup() on parent path '%s' failed, errror = %d", KextLog_Unslide(vnode), path, error);
        return NULLVP;
    }
}

