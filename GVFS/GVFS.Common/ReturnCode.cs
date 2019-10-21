namespace GVFS.Common
{
    public enum ReturnCode
    {
        Success = 0,
        ParsingError = 1,
        RebootRequired = 2,
        GenericError = 3,
        FilterError = 4,
        NullRequestData = 5,
        UnableToRegisterForOfflineIO = 6,
        DehydrateFolderFailures = 7,
    }
}
