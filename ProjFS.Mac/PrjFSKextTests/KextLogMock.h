#ifndef KextLogMock_h
#define KextLogMock_h

inline void KextLog_Error(const char* fmt, ...) {}
inline void KextLog_ErrorVnodeProperties(struct vnode* vnode, const char* fmt, ...) {}
inline void KextLog_FileInfo(struct vnode* vnode, const char* fmt, ...) {}
inline void KextLog_FileNote(struct vnode* vnode, const char* fmt, ...) {}
inline void KextLog_FileError(struct vnode* vnode, const char* fmt, ...) {}

#endif 
