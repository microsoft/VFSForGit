using System;
using System.Collections.Generic;

namespace GVFS.Common.Sparse
{
    /// <summary>
    /// Finds the deleted files and modified folders that a cone change must not reach.
    /// </summary>
    /// <remarks>
    /// <c>git sparse-checkout reapply</c> replaces a sparse-directory entry with the entries
    /// of the directories that the cone now includes. Each new entry starts with the
    /// skip-worktree bit. The virtual file system hook then clears that bit for each modified
    /// path. Git takes that change as an entry that enters the sparse-checkout, and it checks
    /// out each such file that is not in the working directory. For a file that the user
    /// deleted, Git writes the file again.
    /// <para>
    /// Thus the cone must not include the directory of a deleted file that is inside a
    /// sparse-directory entry, or any directory in a modified folder that holds one. A modified
    /// file that exists keeps its content. A modified path that already has its own index entry
    /// is safe, because its skip-worktree bit does not change. A cone that includes only a
    /// parent directory is also safe: the deeper directory stays a sparse-directory entry.
    /// </para>
    /// <para>
    /// This type does no I/O.
    /// </para>
    /// </remarks>
    public static class SparseDirectoryExpansionGuard
    {
        /// <summary>
        /// Return the modified paths that are inside a sparse-directory entry, or that are a
        /// folder that holds one. These paths have no index entry of their own.
        /// </summary>
        /// <param name="modifiedPaths">Modified-path entries. A folder entry ends with a slash.</param>
        /// <param name="sparseDirectories">The sparse-directory entries of the index, with or without a trailing slash.</param>
        /// <param name="ignoreCase">The repository's <c>core.ignoreCase</c> value.</param>
        public static List<string> FindHiddenModifiedPaths(
            IEnumerable<string> modifiedPaths,
            IReadOnlyList<string> sparseDirectories,
            bool ignoreCase)
        {
            ArgumentNullException.ThrowIfNull(modifiedPaths);
            ArgumentNullException.ThrowIfNull(sparseDirectories);

            GitPathComparer comparer = GitPathComparer.GetComparer(ignoreCase);
            List<string> directories = new List<string>(sparseDirectories.Count);
            foreach (string sparseDirectory in sparseDirectories)
            {
                string directory = sparseDirectory == null ? string.Empty : ConePath.Normalize(sparseDirectory, out _);
                if (directory.Length != 0)
                {
                    directories.Add(directory);
                }
            }

            List<string> hidden = new List<string>();
            if (directories.Count == 0)
            {
                return hidden;
            }

            foreach (string modifiedPath in modifiedPaths)
            {
                string path = modifiedPath == null ? string.Empty : ConePath.Normalize(modifiedPath, out _);
                if (path.Length == 0)
                {
                    continue;
                }

                foreach (string directory in directories)
                {
                    if (comparer.Equals(path, directory) ||
                        comparer.IsDescendantOf(path, directory) ||
                        (modifiedPath.EndsWith(GVFSConstants.GitPathSeparatorString, StringComparison.Ordinal) && comparer.IsDescendantOf(directory, path)))
                    {
                        hidden.Add(modifiedPath);
                        break;
                    }
                }
            }

            return hidden;
        }

        /// <summary>
        /// Return the first path in <paramref name="unsafePaths"/> that the cone reaches, or
        /// null. The cone reaches a file when it includes the directory of the file. The cone
        /// reaches a folder (a path that ends with a slash) when it includes the folder or a
        /// directory in it.
        /// </summary>
        public static string FindReachedPath(ConePatternSet cone, IEnumerable<string> unsafePaths, bool ignoreCase)
        {
            ArgumentNullException.ThrowIfNull(cone);
            ArgumentNullException.ThrowIfNull(unsafePaths);

            GitPathComparer comparer = GitPathComparer.GetComparer(ignoreCase);
            foreach (string unsafePath in unsafePaths)
            {
                if (unsafePath == null)
                {
                    continue;
                }

                string path = ConePath.Normalize(unsafePath, out bool isFolder);
                if (path.Length == 0)
                {
                    continue;
                }

                bool reached = isFolder
                    ? IncludesDirectoryAtOrBelow(cone, path, comparer)
                    : IncludesDirectory(cone, ConePath.GetParent(path), comparer);
                if (reached)
                {
                    return unsafePath;
                }
            }

            return null;
        }

        /// <summary>Return true when the cone includes the files directly in the directory.</summary>
        private static bool IncludesDirectory(ConePatternSet cone, string directory, GitPathComparer comparer)
        {
            if (directory.Length == 0)
            {
                return true;
            }

            foreach (string parentOnly in cone.ParentOnlyDirectories)
            {
                if (comparer.Equals(parentOnly, directory))
                {
                    return true;
                }
            }

            foreach (string recursive in cone.RecursiveDirectories)
            {
                if (comparer.Equals(recursive, directory) || comparer.IsDescendantOf(directory, recursive))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Return true when the cone includes the folder, or any directory in it.</summary>
        private static bool IncludesDirectoryAtOrBelow(ConePatternSet cone, string folder, GitPathComparer comparer)
        {
            foreach (string parentOnly in cone.ParentOnlyDirectories)
            {
                if (comparer.Equals(parentOnly, folder) || comparer.IsDescendantOf(parentOnly, folder))
                {
                    return true;
                }
            }

            foreach (string recursive in cone.RecursiveDirectories)
            {
                if (comparer.Equals(recursive, folder) ||
                    comparer.IsDescendantOf(recursive, folder) ||
                    comparer.IsDescendantOf(folder, recursive))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
