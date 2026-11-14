using System;
using System.Collections.Generic;
using System.Text;

namespace GVFS.Common.Sparse
{
    /// <summary>
    /// Reads a cone-mode <c>info/sparse-checkout</c> file into a <see cref="ConePatternSet"/>,
    /// and finds the resolved paths that the cone does not cover.
    /// </summary>
    /// <remarks>
    /// A widen request for paths that the cone covers cannot change the cone. The pre-command
    /// hook uses this type to find those requests and not send them, so that a command that
    /// names only in-cone paths does not wait for the mount.
    /// <para>
    /// The parse is strict and follows git's cone-mode reader (<c>add_pattern_to_hashsets</c>
    /// in <c>dir.c</c>). Like git, it skips empty lines and comment lines. The file must start
    /// with the <c>/*</c> and <c>!/*/</c> header lines.
    /// Each later line must be a positive directory pattern or a parent-only marker that
    /// follows the positive pattern for the same directory, and no directory can repeat.
    /// For any other file, <see cref="TryParseConeFile"/> fails, and the caller must not
    /// trust the file.
    /// </para>
    /// </remarks>
    public sealed class ConeCoverage
    {
        private const string HeaderInclude = "/*";
        private const string HeaderExclude = "!/*/";
        private const string ParentOnlySuffix = "/*/";

        private static readonly UTF8Encoding Utf8WithoutBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private readonly ConePathMatcher matcher;
        private readonly HashSet<string> recursiveDirectories;

        public ConeCoverage(ConePatternSet cone)
        {
            ArgumentNullException.ThrowIfNull(cone);

            this.matcher = new ConePathMatcher(cone);
            this.recursiveDirectories = new HashSet<string>(cone.RecursiveDirectories, GitPathComparer.GetComparer(cone.IgnoreCase));
        }

        /// <summary>
        /// Decode sparse-checkout file bytes as git reads them: skip a UTF-8 byte order mark and
        /// read the rest as UTF-8. Git does not recognize other byte order marks, so a UTF-16
        /// file does not decode to cone patterns. A byte sequence that is not UTF-8 becomes
        /// U+FFFD, and <see cref="TryParseConeFile"/> does not trust a directory with U+FFFD.
        /// </summary>
        public static string DecodeConeFile(byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);

            int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            return Utf8WithoutBom.GetString(bytes, start, bytes.Length - start);
        }

        /// <summary>
        /// Return true when the content starts with the cone-mode header lines <c>/*</c> and
        /// <c>!/*/</c>. Git cannot use a sparse index without cone mode, so content without
        /// the header cannot need a widen.
        /// </summary>
        public static bool HasConeHeader(string content)
        {
            if (content == null)
            {
                return false;
            }

            int position = 0;
            return TryReadLine(content, ref position, out string first) && first == HeaderInclude &&
                TryReadLine(content, ref position, out string second) && second == HeaderExclude;
        }

        /// <summary>
        /// Parse cone-mode sparse-checkout file content. Return false when the content is not
        /// a cone file that git and GVFS read with the same meaning.
        /// </summary>
        /// <param name="content">The file content.</param>
        /// <param name="ignoreCase">The repository's <c>core.ignoreCase</c> value.</param>
        /// <param name="cone">The cone, when the method returns true. Otherwise null.</param>
        public static bool TryParseConeFile(string content, bool ignoreCase, out ConePatternSet cone)
        {
            cone = null;
            if (!HasConeHeader(content))
            {
                return false;
            }

            GitPathComparer comparer = GitPathComparer.GetComparer(ignoreCase);
            List<string> positiveDirectories = new List<string>();
            HashSet<string> positives = new HashSet<string>(comparer);
            HashSet<string> parentOnly = new HashSet<string>(comparer);

            int position = 0;
            TryReadLine(content, ref position, out _);
            TryReadLine(content, ref position, out _);
            while (TryReadLine(content, ref position, out string line))
            {
                if (line[0] == '!')
                {
                    // Git requires the positive pattern before its parent-only marker.
                    if (!TryParseParentOnly(line, out string directory) ||
                        !positives.Contains(directory) ||
                        !parentOnly.Add(directory))
                    {
                        return false;
                    }
                }
                else
                {
                    if (!TryParsePositive(line, out string directory) || !positives.Add(directory))
                    {
                        return false;
                    }

                    positiveDirectories.Add(directory);
                }
            }

            List<string> parentOnlyDirectories = new List<string>();
            List<string> recursiveDirectories = new List<string>();
            foreach (string directory in positiveDirectories)
            {
                if (parentOnly.Contains(directory))
                {
                    parentOnlyDirectories.Add(directory);
                }
                else
                {
                    recursiveDirectories.Add(directory);
                }
            }

            try
            {
                cone = new ConePatternSet(parentOnlyDirectories, recursiveDirectories, ignoreCase);
            }
            catch (ArgumentException)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Return true when a resolved git-form path is in the cone. A file path is covered
        /// when git includes it (see <see cref="ConePathMatcher.IsInCone"/>). A folder path, which
        /// ends with a slash, is covered only when its whole subtree is in the cone: the folder
        /// or one of its ancestors is a recursive directory.
        /// </summary>
        public bool IsCovered(string resolvedGitPath)
        {
            ArgumentNullException.ThrowIfNull(resolvedGitPath);

            string path = ConePath.Normalize(resolvedGitPath, out bool isFolder);
            if (!isFolder)
            {
                return this.matcher.IsInCone(path);
            }

            for (string directory = path; directory.Length != 0; directory = ConePath.GetParent(directory))
            {
                if (this.recursiveDirectories.Contains(directory))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Return the resolved git-form paths that the cone does not cover, in their input order.
        /// </summary>
        public IReadOnlyList<string> GetUncoveredPaths(IEnumerable<string> resolvedGitPaths)
        {
            ArgumentNullException.ThrowIfNull(resolvedGitPaths);

            List<string> uncovered = new List<string>();
            foreach (string path in resolvedGitPaths)
            {
                if (!this.IsCovered(path))
                {
                    uncovered.Add(path);
                }
            }

            return uncovered;
        }

        private static bool TryParsePositive(string line, out string directory)
        {
            directory = null;

            // A positive directory pattern is "/<escaped-directory>/".
            if (line.Length < 3 ||
                line[0] != GVFSConstants.GitPathSeparator ||
                line[line.Length - 1] != GVFSConstants.GitPathSeparator)
            {
                return false;
            }

            return TryUnescapeDirectory(line.Substring(1, line.Length - 2), out directory);
        }

        private static bool TryParseParentOnly(string line, out string directory)
        {
            directory = null;

            // A parent-only marker is "!/<escaped-directory>/*/".
            if (line.Length < 2 + 1 + ParentOnlySuffix.Length ||
                line[1] != GVFSConstants.GitPathSeparator ||
                !line.EndsWith(ParentOnlySuffix, StringComparison.Ordinal))
            {
                return false;
            }

            return TryUnescapeDirectory(line.Substring(2, line.Length - 2 - ParentOnlySuffix.Length), out directory);
        }

        /// <summary>
        /// Reverse <see cref="ConeFileWriter.EscapePattern"/>. Fail on an unescaped wildcard
        /// character or on a backslash that does not escape one, because git does not read
        /// either as a literal cone directory.
        /// </summary>
        private static bool TryUnescapeDirectory(string escaped, out string directory)
        {
            directory = null;

            StringBuilder builder = new StringBuilder(escaped.Length);
            for (int i = 0; i < escaped.Length; i++)
            {
                char character = escaped[i];
                if (character == '\\')
                {
                    if (i + 1 >= escaped.Length || !IsGlobSpecial(escaped[i + 1]))
                    {
                        return false;
                    }

                    i++;
                    builder.Append(escaped[i]);
                }
                else if (IsGlobSpecial(character))
                {
                    return false;
                }
                else
                {
                    builder.Append(character);
                }
            }

            directory = builder.ToString();

            // DecodeConeFile replaces bytes that are not UTF-8 with U+FFFD, and git compares
            // the original bytes. Such a directory cannot be compared, so do not trust the file.
            return directory.IndexOf('\uFFFD') < 0 && ConePatternSet.IsValidDirectory(directory);
        }

        /// <summary>
        /// Read the next line that is not empty and not a comment, without its line ending, and
        /// move past it. Git's reader skips the same lines.
        /// </summary>
        private static bool TryReadLine(string content, ref int position, out string line)
        {
            while (position < content.Length)
            {
                int end = content.IndexOf('\n', position);
                if (end < 0)
                {
                    end = content.Length;
                }

                line = content.Substring(position, end - position).TrimEnd('\r');
                position = end + 1;
                if (line.Length != 0 && line[0] != '#')
                {
                    return true;
                }
            }

            line = null;
            return false;
        }

        private static bool IsGlobSpecial(char character)
        {
            return character == '*' || character == '?' || character == '[' || character == '\\';
        }
    }
}
