namespace GVFS.Common.Database
{
    public interface IPlaceholderData
    {
        string Path { get; }
        string Sha { get; }
        bool IsFolder { get; }
        bool IsExpandedFolder { get; }
        bool IsPossibleTombstoneFolder { get; }
    }
}
