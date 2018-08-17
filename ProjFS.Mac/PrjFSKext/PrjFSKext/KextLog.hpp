#ifndef KextLog_h
#define KextLog_h

#include "PrjFSCommon.h"
#include "PrjFSClasses.hpp"
#include "PrjFSLogClientShared.h"
#include <os/log.h>

extern os_log_t __prjfs_log;

bool KextLog_Init();
void KextLog_Cleanup();

#define KextLog_Error(format, ...) KextLog_Printf(KEXTLOG_ERROR, format, ##__VA_ARGS__)
#define KextLog_Info(format, ...) KextLog_Printf(KEXTLOG_INFO, format, ##__VA_ARGS__)
#define KextLog_Note(format, ...) KextLog_Printf(KEXTLOG_NOTE, format, ##__VA_ARGS__)

bool KextLog_RegisterUserClient(PrjFSLogUserClient* userClient);
void KextLog_DeregisterUserClient(PrjFSLogUserClient* userClient);
void KextLog_Printf(KextLog_Level loglevel, const char* fmt, ...)  __printflike(2,3);


// Helper macros/function for logging with file paths. Note that the path must
// be the last % format code in the format string, but the vnode is the first
// argument following the format string. An unfortunate implementation detail!
struct vnode;
extern "C" int vn_getpath(struct vnode* vp, char* pathbuf, int* len);
extern "C" uint32_t vnode_vid(struct vnode* vp);
template <typename... args>
    void KextLogFile_Printf(KextLog_Level loglevel, struct vnode* vnode, const char* fmt, args... a)
    {
        char vnodePath[PrjFSMaxPath] = "";
        int vnodePathLength = PrjFSMaxPath;
        vn_getpath(vnode, vnodePath, &vnodePathLength);
        int vid = vnode_vid(vnode);
        
        KextLog_Printf(loglevel, fmt, a..., vnodePath, vid);
    }

// The dummy _os_log_verify_format_str() expression here is for using its
// compile time printf format checking, as template varargs can't be annotated
// as __printflike.
// The %s at the end of the format string for the vnode path is implicit.
#define KextLog_FileError(vnode, format, ...) ({ _os_log_verify_format_str(format, ##__VA_ARGS__); KextLogFile_Printf(KEXTLOG_ERROR, vnode, format " (vnode path: '%s')", ##__VA_ARGS__); })
#define KextLog_FileInfo(vnode, format, ...)  ({ _os_log_verify_format_str(format, ##__VA_ARGS__); KextLogFile_Printf(KEXTLOG_INFO, vnode, format " (vnode path: '%s')", ##__VA_ARGS__); })
#define KextLog_FileNote(vnode, format, ...)  ({ _os_log_verify_format_str(format, ##__VA_ARGS__); KextLogFile_Printf(KEXTLOG_NOTE, vnode, format " (vnode path: '%s', vid: %d)", ##__VA_ARGS__); })

#define KextLog_VnodeOp(vnode, vnodeType, procname, uid, action, message) \
    do { \
        if (VDIR == vnodeType) \
        { \
            KextLog_FileNote( \
                vnode, \
                message ". Proc: %s. Uid: %d. Directory vnode action: %s%s%s%s%s%s%s%s%s%s%s%s%s \n    ", \
                procname, \
                uid, \
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
            KextLog_FileNote( \
                vnode, \
                message ". Proc: %s. Uid: %d. File vnode action: %s%s%s%s%s%s%s%s%s%s%s%s \n    ", \
                procname, \
                uid, \
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
