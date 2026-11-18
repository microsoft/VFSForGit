using System;
using System.Collections.Generic;

namespace GVFS.Common.Sparse
{
    /// <summary>
    /// Compares git paths the way git compares sparse-checkout cone patterns.
    /// </summary>
    /// <remarks>
    /// Git keys its cone pattern hashmaps with <c>fspathhash</c> and <c>fspathncmp</c>
    /// (<c>dir.c</c>). When <c>core.ignoreCase</c> is false they compare bytes exactly. When it
    /// is true they fold ASCII letters only, through <c>strihash</c> and <c>strncasecmp</c>,
    /// so <c>A</c> matches <c>a</c> but <c>Ä</c> does not match <c>ä</c>.
    /// <see cref="StringComparer.OrdinalIgnoreCase"/> also folds non-ASCII letters, so it
    /// does not agree with git and must not be used for cone paths.
    /// </remarks>
    internal sealed class GitPathComparer : IEqualityComparer<string>
    {
        private GitPathComparer(bool ignoreCase)
        {
            this.IgnoreCase = ignoreCase;
        }

        /// <summary>Exact comparison, as git does when <c>core.ignoreCase</c> is false.</summary>
        public static GitPathComparer Ordinal { get; } = new GitPathComparer(ignoreCase: false);

        /// <summary>ASCII-only case folding, as git does when <c>core.ignoreCase</c> is true.</summary>
        public static GitPathComparer IgnoreAsciiCase { get; } = new GitPathComparer(ignoreCase: true);

        public bool IgnoreCase { get; }

        public static GitPathComparer GetComparer(bool ignoreCase)
        {
            return ignoreCase ? IgnoreAsciiCase : Ordinal;
        }

        public bool Equals(string x, string y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x == null || y == null || x.Length != y.Length)
            {
                return false;
            }

            return this.RegionEquals(x, y, y.Length);
        }

        public int GetHashCode(string path)
        {
            ArgumentNullException.ThrowIfNull(path);

            if (!this.IgnoreCase)
            {
                return StringComparer.Ordinal.GetHashCode(path);
            }

            HashCode hash = default;
            foreach (char character in path)
            {
                hash.Add(FoldAscii(character));
            }

            return hash.ToHashCode();
        }

        /// <summary>
        /// Return true when <paramref name="path"/> is strictly below the directory
        /// <paramref name="potentialAncestor"/>. Both paths are in git form, without leading or
        /// trailing separators.
        /// </summary>
        public bool IsDescendantOf(string path, string potentialAncestor)
        {
            return path.Length > potentialAncestor.Length
                && path[potentialAncestor.Length] == GVFSConstants.GitPathSeparator
                && this.RegionEquals(path, potentialAncestor, potentialAncestor.Length);
        }

        private static char FoldAscii(char character)
        {
            return character >= 'A' && character <= 'Z' ? (char)(character + ('a' - 'A')) : character;
        }

        private bool RegionEquals(string left, string right, int length)
        {
            if (!this.IgnoreCase)
            {
                return string.CompareOrdinal(left, 0, right, 0, length) == 0;
            }

            for (int i = 0; i < length; i++)
            {
                if (FoldAscii(left[i]) != FoldAscii(right[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
