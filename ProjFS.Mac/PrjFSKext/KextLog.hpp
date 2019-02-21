#ifndef KextLog_h
#define KextLog_h

#include "public/PrjFSCommon.h"
#include "PrjFSClasses.hpp"
#include "public/PrjFSLogClientShared.h"
#include "kernel-header-wrappers/vnode.h"
#include "kernel-header-wrappers/mount.h"
#include <os/log.h>

// Redeclared as printf-like to get format string warnings on assertf()
extern "C" void panic(const char* fmt, ...) __printflike(1, 2);

bool KextLog_Init();
void KextLog_Cleanup();

#define KextLog_Error(format, ...) KextLog_Printf(KEXTLOG_ERROR, format, ##__VA_ARGS__)
#define KextLog_Info(format, ...) KextLog_Printf(KEXTLOG_INFO, format, ##__VA_ARGS__)
#define KextLog(format, ...) KextLog_Printf(KEXTLOG_DEFAULT, format, ##__VA_ARGS__)

bool KextLog_RegisterUserClient(PrjFSLogUserClient* userClient);
void KextLog_DeregisterUserClient(PrjFSLogUserClient* userClient);
void KextLog_Printf(KextLog_Level loglevel, const char* fmt, ...)  __printflike(2,3);
// Prepares a kernel pointer for printing to user space without revealing the
// genuine kernel-space address, which would be a security issue.
const void* KextLog_Unslide(const void* pointer);


// Helper macros/function for logging with file paths. Note that the path must
// be the last % format code in the format string, but the vnode is the first
// argument following the format string. An unfortunate implementation detail,
// but not too problematic if you just use the macros and not the function directly.
//
// The reason for the helper function/macro split is that we want to encourage
// the compiler to create a new stack frame to hold the path buffer in order to
// avoid bloating the caller's stack frame; we can't do this in a macro.
// We need the macros below for the format string concatenation; we can't do
// this in a function.
struct vnode;
extern "C" int vn_getpath(struct vnode *vp, char *pathbuf, int *len);
template <typename... args>
    void KextLogFile_Printf(KextLog_Level loglevel, struct vnode* vnode, const char* fmt, args... a)
    {
        char vnodePath[PrjFSMaxPath] = "";
        int vnodePathLength = PrjFSMaxPath;
        vn_getpath(vnode, vnodePath, &vnodePathLength);
        KextLog_Printf(loglevel, fmt, a..., vnodePath);
    }

// The dummy _os_log_verify_format_str() expression here is for using its
// compile time printf format checking, as template varargs can't be annotated
// as __printflike.
// The %s at the end of the format string for the vnode path is implicit.
#define KextLog_FileError(vnode, format, ...) ({ _os_log_verify_format_str(format, ##__VA_ARGS__); KextLogFile_Printf(KEXTLOG_ERROR, vnode, format " (vnode path: '%s')", ##__VA_ARGS__); })
#define KextLog_FileInfo(vnode, format, ...)  ({ _os_log_verify_format_str(format, ##__VA_ARGS__); KextLogFile_Printf(KEXTLOG_INFO, vnode, format " (vnode path: '%s')", ##__VA_ARGS__); })
#define KextLog_File(vnode, format, ...)  ({ _os_log_verify_format_str(format, ##__VA_ARGS__); KextLogFile_Printf(KEXTLOG_DEFAULT, vnode, format " (vnode path: '%s')", ##__VA_ARGS__); })


// See comments for KextLogFile_Printf() above for rationale.
template <typename... args>
    void KextLog_PrintfVnodePathAndProperties(KextLog_Level loglevel, struct vnode* vnode, const char* fmt, args... a)
    {
        char vnodePath[PrjFSMaxPath] = "";
        int vnodePathLength = PrjFSMaxPath;
        vn_getpath(vnode, vnodePath, &vnodePathLength);
        
        const char* name = vnode_getname(vnode);
        mount_t mount = vnode_mount(vnode);
        vfsstatfs* vfsStat = mount != nullptr ? vfs_statfs(mount) : nullptr;
        
        KextLog_Printf(loglevel, fmt, a..., vnodePath, name ?: "[NULL]", vnode_vtype(vnode), vnode_isrecycled(vnode) ? "yes" : "no", vfsStat ? vfsStat->f_mntonname : "[NULL]");
 
        if (name != nullptr)
        {
            // Cancels out the vnode_getname() call above, which incremented the refcount on the returned string.
            vnode_putname(name);
        }
    }

#define KextLog_ErrorVnodeProperties(vnode, format, ...) \
    ({ \
        _os_log_verify_format_str(format, ##__VA_ARGS__); \
        KextLog_PrintfVnodeProperties(KEXTLOG_ERROR, vnode, format " (vnode name: '%s', type: %d, recycling: %s, mount point mounted at path '%s')", ##__VA_ARGS__); \
    })


// See comments for KextLogFile_Printf() above for rationale.
template <typename... args>
    void KextLog_PrintfVnodeProperties(KextLog_Level loglevel, struct vnode* vnode, const char* fmt, args... a)
    {
        const char* name = vnode_getname(vnode);
        mount_t mount = vnode_mount(vnode);
        vfsstatfs* vfsStat = mount != nullptr ? vfs_statfs(mount) : nullptr;
        
        KextLog_Printf(loglevel, fmt, a..., name ?: "[NULL]", vnode_vtype(vnode), vnode_isrecycled(vnode) ? "yes" : "no", vfsStat ? vfsStat->f_mntonname : "[NULL]");
 
        if (name != nullptr)
        {
            // Cancels out the vnode_getname() call above, which incremented the refcount on the returned string.
            vnode_putname(name);
        }
    }

#define KextLog_ErrorVnodePathAndProperties(vnode, format, ...) \
    ({ \
        _os_log_verify_format_str(format, ##__VA_ARGS__); \
        KextLog_PrintfVnodePathAndProperties(KEXTLOG_ERROR, vnode, format " (vnode path: '%s', name: '%s', type: %d, recycling: %s, mount point mounted at path '%s')", ##__VA_ARGS__); \
    })


#define KextLog_VnodeOp(vnode, vnodeType, procname, action, message) \
    do { \
        if (VDIR == vnodeType) \
        { \
            KextLog_File( \
                vnode, \
                message ". Proc name: %s. Directory vnode action: %s%s%s%s%s%s%s%s%s%s%s%s%s \n    ", \
                procname, \
                (action & KAUTH_VNODE_LIST_DIRECTORY)       ? " \n    KAUTH_VNODE_LIST_DIRECTORY" : "", \
                (action & KAUTH_VNODE_ADD_FILE)             ? " \n    KAUTH_VNODE_ADD_FILE" : "", \
                (action & KAUTH_VNODE_SEARCH)               ? " \n    KAUTH_VNODE_SEARCH" : "", \
                (action & KAUTH_VNODE_DELETE)               ? " \n    KAUTH_VNODE_DELETE" : "", \
                (action & KAUTH_VNODE_ADD_SUBDIRECTORY)     ? " \n    KAUTH_VNODE_ADD_SUBDIRECTORY" : "", \
                (action & KAUTH_VNODE_DELETE_CHILD)         ? " \n    KAUTH_VNODE_DELETE_CHILD" : "", \
                (action & KAUTH_VNODE_READ_ATTRIBUTES)      ? " \n    KAUTH_VNODE_READ_ATTRIBUTES" : "", \
                (action & KAUTH_VNODE_WRITE_ATTRIBUTES)     ? " \n    KAUTH_VNODE_WRITE_ATTRIBUTES" : "", \
                (action & KAUTH_VNODE_READ_EXTATTRIBUTES)   ? " \n    KAUTH_VNODE_READ_EXTATTRIBUTES" : "", \
                (action & KAUTH_VNODE_WRITE_EXTATTRIBUTES)  ? " \n    KAUTH_VNODE_WRITE_EXTATTRIBUTES" : "", \
                (action & KAUTH_VNODE_READ_SECURITY)        ? " \n    KAUTH_VNODE_READ_SECURITY" : "", \
                (action & KAUTH_VNODE_WRITE_SECURITY)       ? " \n    KAUTH_VNODE_WRITE_SECURITY" : "", \
                (action & KAUTH_VNODE_TAKE_OWNERSHIP)       ? " \n    KAUTH_VNODE_TAKE_OWNERSHIP" : ""); \
        } \
        else \
        { \
            KextLog_File( \
                vnode, \
                message ". Proc name: %s. File vnode action: %s%s%s%s%s%s%s%s%s%s%s%s \n    ", \
                procname, \
                (action & KAUTH_VNODE_READ_DATA)            ? " \n    KAUTH_VNODE_READ_DATA" : "", \
                (action & KAUTH_VNODE_WRITE_DATA)           ? " \n    KAUTH_VNODE_WRITE_DATA" : "", \
                (action & KAUTH_VNODE_EXECUTE)              ? " \n    KAUTH_VNODE_EXECUTE" : "", \
                (action & KAUTH_VNODE_DELETE)               ? " \n    KAUTH_VNODE_DELETE" : "", \
                (action & KAUTH_VNODE_APPEND_DATA)          ? " \n    KAUTH_VNODE_APPEND_DATA" : "", \
                (action & KAUTH_VNODE_READ_ATTRIBUTES)      ? " \n    KAUTH_VNODE_READ_ATTRIBUTES" : "", \
                (action & KAUTH_VNODE_WRITE_ATTRIBUTES)     ? " \n    KAUTH_VNODE_WRITE_ATTRIBUTES" : "", \
                (action & KAUTH_VNODE_READ_EXTATTRIBUTES)   ? " \n    KAUTH_VNODE_READ_EXTATTRIBUTES" : "", \
                (action & KAUTH_VNODE_WRITE_EXTATTRIBUTES)  ? " \n    KAUTH_VNODE_WRITE_EXTATTRIBUTES" : "", \
                (action & KAUTH_VNODE_READ_SECURITY)        ? " \n    KAUTH_VNODE_READ_SECURITY" : "", \
                (action & KAUTH_VNODE_WRITE_SECURITY)       ? " \n    KAUTH_VNODE_WRITE_SECURITY" : "", \
                (action & KAUTH_VNODE_TAKE_OWNERSHIP)       ? " \n    KAUTH_VNODE_TAKE_OWNERSHIP" : ""); \
        } \
    } while (0)

#endif /* KextLog_h */
