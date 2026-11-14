using System;
using System.Collections.Generic;

namespace GVFS.Common.Sparse
{
    /// <summary>
    /// Maps a set of modified paths to a git cone-mode sparse-checkout pattern set.
    /// </summary>
    /// <remarks>
    /// A modified file contributes parent-only patterns for its parent directory and
    /// every ancestor directory, so a single edited file never pulls its whole subtree
    /// into the index. A modified folder contributes one recursive pattern for the whole
    /// subtree, which matches the existing recursive meaning of a folder entry in the
    /// modified-paths list.
    /// <para>
    /// GVFS builds the patterns itself and does not call <c>git sparse-checkout set</c>.
    /// In cone mode, <c>set</c> passes each input directory through
    /// <c>strbuf_to_cone_pattern</c> to <c>insert_recursive_pattern</c>
    /// (<c>builtin/sparse-checkout.c</c>), so every input becomes a recursive pattern. To
    /// include one modified file, <c>set</c> would have to include its whole parent
    /// directory recursively, with every file below it.
    /// </para>
    /// <para>
    /// This type performs no I/O. The output is sorted ordinally, as git's
    /// <c>write_cone_to_file</c> sorts with <c>strcmp</c>. Directory de-duplication uses
    /// <see cref="GitPathComparer"/>, so it must use the same case rule as the repository's
    /// <c>core.ignoreCase</c>. If git reads two directories that differ only in case while
    /// <c>core.ignoreCase</c> is true, it reports a repeated pattern and turns off cone
    /// matching, and the sparse index cannot stay sparse.
    /// </para>
    /// </remarks>
    public static class ConeBuilder
    {
        /// <summary>
        /// Build a cone pattern set from modified-path entries.
        /// </summary>
        /// <param name="modifiedPaths">
        /// Modified-path entries. A file entry has no trailing slash; a folder entry ends
        /// in a slash. Entries can use either path separator and can have a leading slash
        /// or duplicate separators; the builder normalizes them. Null entries are skipped.
        /// The repo root itself contributes no directory pattern.
        /// </param>
        /// <param name="ignoreCase">
        /// The repository's <c>core.ignoreCase</c> value. When true, directories that differ
        /// only in ASCII case are one directory, and the output keeps the ordinally smallest
        /// spelling, whatever the input order.
        /// </param>
        /// <exception cref="ArgumentException">
        /// A modified path has a directory that a cone-mode file cannot contain (see
        /// <see cref="ConePatternSet.IsValidDirectory"/>). The builder fails instead of writing
        /// a cone that leaves the path out.
        /// </exception>
        public static ConePatternSet BuildFromModifiedPaths(IEnumerable<string> modifiedPaths, bool ignoreCase)
        {
            ArgumentNullException.ThrowIfNull(modifiedPaths);

            GitPathComparer comparer = GitPathComparer.GetComparer(ignoreCase);

            List<(string Path, bool IsFolder)> entries = new List<(string Path, bool IsFolder)>();
            foreach (string rawPath in modifiedPaths)
            {
                if (rawPath == null)
                {
                    continue;
                }

                string cleanPath = ConePath.Normalize(rawPath, out bool isFolder);
                if (cleanPath.Length == 0)
                {
                    // An empty path is the repo root or a bare separator. Root files are
                    // already included by the "/*" header, so there is nothing to add.
                    continue;
                }

                entries.Add((cleanPath, isFolder));
            }

            // Process the entries in ordinal order, and give every directory the first spelling
            // seen for it. When case is ignored, each directory then keeps its ordinally smallest
            // spelling in both lists, and the output does not depend on the input order.
            entries.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));

            Dictionary<string, string> spellings = new Dictionary<string, string>(comparer);
            HashSet<string> recursiveDirectories = new HashSet<string>(comparer);
            HashSet<string> parentOnlyDirectories = new HashSet<string>(comparer);

            foreach ((string Path, bool IsFolder) entry in entries)
            {
                string cleanPath = entry.Path;
                if (entry.IsFolder)
                {
                    ThrowIfInvalidDirectory(cleanPath);
                    recursiveDirectories.Add(Spell(cleanPath, spellings));
                    AddAncestors(cleanPath, parentOnlyDirectories, spellings);
                }
                else
                {
                    string parent = ConePath.GetParent(cleanPath);
                    if (parent.Length != 0)
                    {
                        ThrowIfInvalidDirectory(parent);
                        parentOnlyDirectories.Add(Spell(parent, spellings));
                        AddAncestors(parent, parentOnlyDirectories, spellings);
                    }
                }
            }

            // A recursive directory nested inside another recursive directory is redundant;
            // the ancestor already covers the whole subtree.
            RemoveRedundantRecursiveDirectories(recursiveDirectories, comparer);

            // A parent-only directory that a recursive directory already covers is redundant:
            // either it equals a recursive directory, or it is below one. Parent-only
            // ancestors of a recursive directory are kept, because a recursive directory
            // does not include the direct files of its ancestors.
            RemoveParentOnlyDirectoriesCoveredByRecursive(parentOnlyDirectories, recursiveDirectories, comparer);

            List<string> sortedParents = new List<string>(parentOnlyDirectories);
            sortedParents.Sort(StringComparer.Ordinal);

            List<string> sortedRecursive = new List<string>(recursiveDirectories);
            sortedRecursive.Sort(StringComparer.Ordinal);

            return new ConePatternSet(sortedParents, sortedRecursive, ignoreCase);
        }

        private static void ThrowIfInvalidDirectory(string directory)
        {
            // Check every input directory before the cleanup below can remove it. Otherwise
            // "A/D/../C" would vanish as a descendant of "A/D", and "A/C" would stay outside the
            // cone. The ancestors of a valid directory are also valid.
            if (!ConePatternSet.IsValidDirectory(directory))
            {
                throw new ArgumentException($"'{directory}' is not a directory that a cone-mode sparse-checkout file can contain.");
            }
        }

        private static string Spell(string directory, Dictionary<string, string> spellings)
        {
            if (!spellings.TryGetValue(directory, out string spelling))
            {
                spelling = directory;
                spellings.Add(directory, spelling);
            }

            return spelling;
        }

        private static void AddAncestors(string directory, HashSet<string> parentOnlyDirectories, Dictionary<string, string> spellings)
        {
            // Add every proper ancestor of the directory, but not the directory itself.
            int separator = directory.IndexOf(GVFSConstants.GitPathSeparator);
            while (separator >= 0)
            {
                parentOnlyDirectories.Add(Spell(directory.Substring(0, separator), spellings));
                separator = directory.IndexOf(GVFSConstants.GitPathSeparator, separator + 1);
            }
        }

        private static void RemoveRedundantRecursiveDirectories(HashSet<string> recursiveDirectories, GitPathComparer comparer)
        {
            List<string> toRemove = new List<string>();
            foreach (string candidate in recursiveDirectories)
            {
                foreach (string other in recursiveDirectories)
                {
                    if (comparer.IsDescendantOf(candidate, other))
                    {
                        toRemove.Add(candidate);
                        break;
                    }
                }
            }

            foreach (string redundant in toRemove)
            {
                recursiveDirectories.Remove(redundant);
            }
        }

        private static void RemoveParentOnlyDirectoriesCoveredByRecursive(
            HashSet<string> parentOnlyDirectories,
            HashSet<string> recursiveDirectories,
            GitPathComparer comparer)
        {
            List<string> toRemove = new List<string>();
            foreach (string parent in parentOnlyDirectories)
            {
                if (recursiveDirectories.Contains(parent))
                {
                    toRemove.Add(parent);
                    continue;
                }

                foreach (string recursive in recursiveDirectories)
                {
                    if (comparer.IsDescendantOf(parent, recursive))
                    {
                        toRemove.Add(parent);
                        break;
                    }
                }
            }

            foreach (string covered in toRemove)
            {
                parentOnlyDirectories.Remove(covered);
            }
        }
    }
}
