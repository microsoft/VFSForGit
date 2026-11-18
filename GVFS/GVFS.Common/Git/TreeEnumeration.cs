using System;

namespace GVFS.Common.Git
{
    /// <summary>
    /// The result of an attempt to read the entries of a tree object from the local object store.
    /// </summary>
    public enum TreeEnumerationResult
    {
        /// <summary>
        /// The object is a tree. The visitor received every entry, in tree order.
        /// </summary>
        Success,

        /// <summary>
        /// The object is not in the local object store. The visitor received no entries.
        /// A download can make the object available.
        /// </summary>
        MissingTree,

        /// <summary>
        /// The object is in the local object store, but it is not a tree. The visitor received no entries.
        /// </summary>
        NotTree,

        /// <summary>
        /// The object is in the local object store, but it cannot be read. The visitor can have
        /// received some entries before the failure. The caller must discard them.
        /// </summary>
        CorruptTree,

        /// <summary>
        /// The repository is not available, for example because it is shutting down.
        /// The visitor received no entries.
        /// </summary>
        RepositoryUnavailable,
    }

    /// <summary>
    /// Receives one entry of a tree object.
    /// </summary>
    /// <param name="name">The UTF-8 name of the entry. It is valid only during the call.</param>
    /// <param name="objectId">The 20-byte object id of the entry. It is valid only during the call.</param>
    /// <param name="fileMode">
    /// The git file mode of the entry, for example 0100644 (file), 0100755 (executable),
    /// 0120000 (symbolic link), 0160000 (gitlink), or 040000 (tree).
    /// </param>
    public delegate void TreeEntryVisitor(ReadOnlySpan<byte> name, ReadOnlySpan<byte> objectId, uint fileMode);
}
