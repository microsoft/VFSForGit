#ifndef KauthHandlerHelper_h
#define KauthHandlerHelper_h


static errno_t GetVNodeAttributes(vnode_t vn, vfs_context_t _Nonnull context, struct vnode_attr* attrs)
{
    VATTR_INIT(attrs);
    VATTR_WANTED(attrs, va_flags);
    
    return vnode_getattr(vn, attrs, context);
}

static bool TryReadVNodeFileFlags(vnode_t vn, vfs_context_t _Nonnull context, uint32_t* flags)
{
    struct vnode_attr attributes = {};
    *flags = 0;
    errno_t err = GetVNodeAttributes(vn, context, &attributes);
    if (0 != err)
    {
        // TODO(Mac): May fail on some file system types? Perhaps we should early-out depending on mount point anyway.
        // We should also consider:
        //   - Logging this error
        //   - Falling back on vnode lookup (or custom cache) to determine if file is in the root
        //   - Assuming files are empty if we can't read the flags
        KextLog_FileError(vn, "ReadVNodeFileFlags: GetVNodeAttributes failed with error %d; vnode type: %d, recycled: %s", err, vnode_vtype(vn), vnode_isrecycled(vn) ? "yes" : "no");
        return false;
    }
    
    assert(VATTR_IS_SUPPORTED(&attributes, va_flags));
    *flags = attributes.va_flags;
    return true;
}

#endif
