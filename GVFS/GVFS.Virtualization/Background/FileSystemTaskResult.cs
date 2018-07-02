namespace GVFS.Virtualization.Background
{
    public enum FileSystemTaskResult
    {
        Invalid = 0,

        Success,
        RetryableError,
        FatalError
    }
}
