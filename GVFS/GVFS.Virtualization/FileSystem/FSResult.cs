namespace GVFS.Virtualization.FileSystem
{
    public enum FSResult
    {
        Invalid = 0,
        Ok,
        IOError,
        DirectoryNotEmpty,
        FileOrPathNotFound,
        IoReparseTagNotHandled,
        VirtualizationInvalidOperation,
        GenericProjFSError,
    }
}
