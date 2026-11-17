namespace GVFS.Common.Sparse
{
    /// <summary>
    /// The result of an attempt to return a modified file that matches HEAD to a placeholder.
    /// </summary>
    public enum CleanFileDehydrateResult
    {
        /// <summary>
        /// The file is a placeholder for the expected blob again, and it is not a modified path.
        /// </summary>
        Dehydrated,

        /// <summary>The modified paths do not have the file. Nothing changed.</summary>
        NotModifiedPath,

        /// <summary>The working tree does not have the file. Nothing changed.</summary>
        NotFound,

        /// <summary>
        /// Another process has the file open in a way that prevents the check. Nothing changed.
        /// </summary>
        InUse,

        /// <summary>
        /// The path no longer names the file that was checked, or a delete of the file is
        /// pending: for example, a process renamed another file over it. Nothing changed.
        /// </summary>
        Replaced,

        /// <summary>The bytes on disk are not the expected blob. Nothing changed.</summary>
        ContentDiffers,

        /// <summary>
        /// The virtualization layer did not convert the file. The file and its modified path
        /// did not change.
        /// </summary>
        PlaceholderUpdateFailed,

        /// <summary>
        /// The file is a placeholder for the expected blob, but it is still a modified path. Git
        /// still sees the file, so nothing is lost.
        /// </summary>
        ModifiedPathNotRemoved,

        /// <summary>An unexpected error. The file is unchanged or matches the expected blob.</summary>
        Failed,
    }
}
