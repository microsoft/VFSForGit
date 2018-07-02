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
#define KextLog_FileNote(vnode, format, ...)  ({ _os_log_verify_format_str(format, ##__VA_ARGS__); KextLogFile_Printf(KEXTLOG_NOTE, vnode, format " (vnode path: '%s')", ##__VA_ARGS__); })


#endif /* KextLog_h */
