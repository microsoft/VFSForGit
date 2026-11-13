using System;
using System.Collections.Generic;

namespace GVFS.Common.Sparse
{
    /// <summary>
    /// Decides whether a path is inside a cone, with the same rules that git uses for
    /// cone-mode sparse-checkout patterns.
    /// </summary>
    /// <remarks>
    /// The rules follow <c>path_matches_pattern_list</c> in git's <c>dir.c</c>:
    /// <list type="bullet">
    /// <item>A file at the repository root is always in the cone.</item>
    /// <item>A file is in the cone when its parent directory is a parent-only directory.</item>
    /// <item>A file is in the cone when the file path itself, or one of its ancestor
    /// directories, is a recursive directory.</item>
    /// <item>A directory is in the cone when a file directly inside it would be in the cone.
    /// Thus a parent-only directory is in the cone, but its child directories are not, unless
    /// the cone names them.</item>
    /// </list>
    /// Git compares cone paths with ASCII-only case folding when <c>core.ignoreCase</c> is
    /// true (see <see cref="GitPathComparer"/>). The matcher uses the case rule of the
    /// <see cref="ConePatternSet"/> that it is built from.
    /// </remarks>
    public sealed class ConePathMatcher
    {
        private const string DirectoryProbeName = "-";

        private readonly HashSet<string> parentOnlyDirectories;
        private readonly HashSet<string> recursiveDirectories;

        public ConePathMatcher(ConePatternSet cone)
        {
            ArgumentNullException.ThrowIfNull(cone);

            GitPathComparer comparer = GitPathComparer.GetComparer(cone.IgnoreCase);
            this.parentOnlyDirectories = new HashSet<string>(cone.ParentOnlyDirectories, comparer);
            this.recursiveDirectories = new HashSet<string>(cone.RecursiveDirectories, comparer);
        }

        /// <summary>
        /// Return true when git would treat the path as inside the cone.
        /// </summary>
        /// <param name="path">
        /// A repo-relative path. A trailing separator marks a directory. Either path separator
        /// is accepted, and a leading separator is ignored. An empty path is the repository
        /// root, which is always in the cone.
        /// </param>
        public bool IsInCone(string path)
        {
            ArgumentNullException.ThrowIfNull(path);

            string cleanPath = ConePath.Normalize(path, out bool isDirectory);
            if (cleanPath.Length == 0)
            {
                return true;
            }

            // Git checks a directory as if it were a file named "-" directly inside that
            // directory, so the directory itself takes the place of the file's parent.
            string parent;
            if (isDirectory)
            {
                if (this.recursiveDirectories.Contains(cleanPath + GVFSConstants.GitPathSeparatorString + DirectoryProbeName))
                {
                    return true;
                }

                parent = cleanPath;
            }
            else
            {
                if (this.recursiveDirectories.Contains(cleanPath))
                {
                    return true;
                }

                parent = ConePath.GetParent(cleanPath);
                if (parent.Length == 0)
                {
                    return true;
                }
            }

            if (this.parentOnlyDirectories.Contains(parent))
            {
                return true;
            }

            return this.IsSelfOrAncestorRecursive(parent);
        }

        private bool IsSelfOrAncestorRecursive(string directory)
        {
            string current = directory;
            while (current.Length != 0)
            {
                if (this.recursiveDirectories.Contains(current))
                {
                    return true;
                }

                current = ConePath.GetParent(current);
            }

            return false;
        }
    }
}
