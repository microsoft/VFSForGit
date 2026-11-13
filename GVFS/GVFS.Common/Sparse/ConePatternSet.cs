using System;
using System.Collections.Generic;
using System.Linq;

namespace GVFS.Common.Sparse
{
    /// <summary>
    /// The computed cone-mode sparse-checkout pattern set for a worktree. It is the
    /// output of <see cref="ConeBuilder"/> and the input to <see cref="ConeFileWriter"/>
    /// and <see cref="ConePathMatcher"/>.
    /// </summary>
    /// <remarks>
    /// Directory paths are stored in git form: forward slashes, no leading slash, and
    /// no trailing slash (for example <c>A</c> or <c>A/B</c>). A parent-only directory
    /// includes the files directly in that directory but not its subtree. A recursive
    /// directory includes the whole subtree.
    /// <para>
    /// The constructor rejects a directory that git cannot read back from a cone-mode file
    /// as the same directory. If git finds such a pattern, it turns off cone matching, or it
    /// reads extra patterns, and files that GVFS puts in the cone can be outside it. Thus
    /// every set that the constructor accepts serializes to a file that git reads in cone
    /// mode with the same meaning.
    /// </para>
    /// </remarks>
    public class ConePatternSet
    {
        /// <param name="parentOnlyDirectories">Directories whose direct files are in the cone.</param>
        /// <param name="recursiveDirectories">Directories whose whole subtree is in the cone.</param>
        /// <param name="ignoreCase">
        /// The repository's <c>core.ignoreCase</c> value. When true, directories that differ
        /// only in ASCII case are the same directory (see <see cref="GitPathComparer"/>).
        /// </param>
        /// <exception cref="ArgumentException">
        /// A directory is not a valid cone directory (see <see cref="IsValidDirectory"/>), or it
        /// is in the lists more than once under the case rule. Git reports a repeated directory
        /// and turns off cone matching.
        /// </exception>
        public ConePatternSet(IReadOnlyList<string> parentOnlyDirectories, IReadOnlyList<string> recursiveDirectories, bool ignoreCase)
        {
            ArgumentNullException.ThrowIfNull(parentOnlyDirectories);
            ArgumentNullException.ThrowIfNull(recursiveDirectories);

            HashSet<string> seen = new HashSet<string>(GitPathComparer.GetComparer(ignoreCase));
            AddDistinct(parentOnlyDirectories, seen, nameof(parentOnlyDirectories));
            AddDistinct(recursiveDirectories, seen, nameof(recursiveDirectories));

            // Copy the lists, so that a later change to the caller's lists cannot skip the checks.
            this.ParentOnlyDirectories = Array.AsReadOnly(parentOnlyDirectories.ToArray());
            this.RecursiveDirectories = Array.AsReadOnly(recursiveDirectories.ToArray());
            this.IgnoreCase = ignoreCase;
        }

        /// <summary>
        /// Directories whose direct files are in the cone, without their subtrees.
        /// Each emits a positive directory pattern and a negative child-directory pattern.
        /// </summary>
        public IReadOnlyList<string> ParentOnlyDirectories { get; }

        /// <summary>
        /// Directories whose entire subtree is in the cone. Each emits a single positive
        /// directory pattern.
        /// </summary>
        public IReadOnlyList<string> RecursiveDirectories { get; }

        /// <summary>
        /// True when the directories follow git's <c>core.ignoreCase=true</c> rule.
        /// </summary>
        public bool IgnoreCase { get; }

        /// <summary>
        /// Return true when git can read the directory back from a cone-mode file with the
        /// same meaning that GVFS gives it.
        /// </summary>
        /// <remarks>
        /// The directory must be a non-empty git-form path. It must not contain a line break
        /// or NUL, because git reads the file line by line. It must not contain a backslash,
        /// because GVFS reads a backslash as a separator and git does not. It must not contain
        /// an empty, <c>.</c>, or <c>..</c> segment, which git does not allow in a tracked path.
        /// No segment can be <c>*</c>: git's <c>dup_and_filter_pattern</c> removes the escape
        /// and then a trailing <c>/*</c>, so <c>/A/\*/</c> becomes <c>/A</c>. It must be valid
        /// UTF-16, because the file is written as UTF-8.
        /// </remarks>
        public static bool IsValidDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory))
            {
                return false;
            }

            foreach (string segment in directory.Split(GVFSConstants.GitPathSeparator))
            {
                if (segment.Length == 0 || segment == "." || segment == ".." || segment == "*")
                {
                    return false;
                }
            }

            for (int i = 0; i < directory.Length; i++)
            {
                char character = directory[i];
                if (character == '\n' || character == '\r' || character == '\0' || character == '\\')
                {
                    return false;
                }

                if (char.IsHighSurrogate(character) && i + 1 < directory.Length && char.IsLowSurrogate(directory[i + 1]))
                {
                    i++;
                }
                else if (char.IsSurrogate(character))
                {
                    return false;
                }
            }

            return true;
        }

        private static void AddDistinct(IReadOnlyList<string> directories, HashSet<string> seen, string parameterName)
        {
            foreach (string directory in directories)
            {
                if (!IsValidDirectory(directory))
                {
                    throw new ArgumentException($"'{directory}' is not a directory that a cone-mode sparse-checkout file can contain.", parameterName);
                }

                if (!seen.Add(directory))
                {
                    throw new ArgumentException($"'{directory}' is in the cone more than once.", parameterName);
                }
            }
        }
    }
}
