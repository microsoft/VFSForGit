namespace GVFS.Common.Git
{
    /// <summary>A blob that the index and HEAD both have for a path.</summary>
    /// <param name="Sha">The object id of the blob.</param>
    /// <param name="Size">The size of the blob in bytes.</param>
    public readonly record struct HeadBlob(string Sha, long Size);
}
