namespace GVFS.Common.Database
{
    /// <summary>
    /// Interface for holding placeholder information
    /// </summary>
    public interface IPlaceholderData
    {
        string Path { get; }
        string Sha { get; }
        bool IsFolder { get; }
        bool IsExpandedFolder { get; }
        bool IsPossibleTombstoneFolder { get; }
    }
}
