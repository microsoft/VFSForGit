using System;
using System.Collections.Generic;

namespace GVFS.Common.Sparse
{
    /// <summary>
    /// Resolves the literal pathspecs that a Git command names into repo-relative git-form
    /// paths, which <see cref="ConeCoverage"/> and <see cref="ConeBuilder"/> accept.
    /// </summary>
    /// <remarks>
    /// This type does no I/O.
    /// <para>
    /// Only a trailing slash gives a pathspec the shape of a folder. A folder resolves to a
    /// recursive entry, and any other pathspec resolves to a file, which puts only its parent
    /// directory in the cone. Thus an ambiguous <c>dir/subdir</c> is under-widened, not
    /// over-widened. An over-wide recursive entry makes the index larger. An under-wide
    /// entry only makes Git expand the index in memory.
    /// </para>
    /// <para>
    /// The resolver skips a pathspec that it cannot map to one cone entry, so the result can
    /// only under-widen. It skips a magic pathspec (a leading <c>:</c>), a path outside the
    /// worktree, the worktree root, a pathspec with a wildcard character in a directory
    /// segment, and a path that <see cref="IsValidGitPath"/> rejects. A wildcard only in the
    /// last segment of a file pathspec is kept: the file's parent directory is still the
    /// right cone entry for it.
    /// </para>
    /// </remarks>
    public static class ConePathspecResolver
    {
        private static readonly char[] WildcardCharacters = new[] { '*', '?', '[' };
        private static readonly string[] ReservedDeviceNames = new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" };

        /// <summary>
        /// Return the directory that relative pathspecs resolve against.
        /// </summary>
        /// <remarks>
        /// Before Git runs the pre-command hook for a command that uses a repository, Git
        /// changes to the top of the worktree and puts the user's subdirectory in the
        /// <c>GIT_PREFIX</c> environment variable. Thus when the hook runs at the worktree
        /// root and <paramref name="gitPrefix"/> is a relative path inside the worktree, the
        /// base directory is the root plus the prefix. In all other cases, the hook's current
        /// directory is the base directory.
        /// </remarks>
        /// <param name="hookCurrentDirectory">The current directory of the hook process (absolute).</param>
        /// <param name="workingDirectoryRoot">The root of the worktree (absolute).</param>
        /// <param name="gitPrefix">The value of <c>GIT_PREFIX</c>, or null when it is not set.</param>
        public static string GetPathspecBaseDirectory(string hookCurrentDirectory, string workingDirectoryRoot, string gitPrefix)
        {
            ArgumentNullException.ThrowIfNull(hookCurrentDirectory);
            ArgumentNullException.ThrowIfNull(workingDirectoryRoot);

            if (string.IsNullOrEmpty(gitPrefix))
            {
                return hookCurrentDirectory;
            }

            string current = TrimTrailingSeparators(ToForwardSlashes(hookCurrentDirectory));
            string root = TrimTrailingSeparators(ToForwardSlashes(workingDirectoryRoot));
            if (!current.Equals(root, StringComparison.OrdinalIgnoreCase))
            {
                // Git did not change to the worktree root, so the prefix does not apply.
                return hookCurrentDirectory;
            }

            // Git writes the prefix as a relative path with a trailing slash. Ignore any other
            // value, so that the base directory cannot leave the worktree.
            string prefix = TrimTrailingSeparators(ToForwardSlashes(gitPrefix));
            if (prefix.Length == 0 || !IsValidGitPath(prefix))
            {
                return hookCurrentDirectory;
            }

            return root + GVFSConstants.GitPathSeparatorString + prefix;
        }

        /// <summary>
        /// Resolve pathspecs to distinct repo-relative git-form paths. A file entry has no
        /// trailing slash. A folder entry keeps its trailing slash. Every result passes
        /// <see cref="IsValidGitPath"/>.
        /// </summary>
        /// <param name="pathspecBaseDirectory">
        /// The directory that relative pathspecs resolve against (absolute). See
        /// <see cref="GetPathspecBaseDirectory"/>.
        /// </param>
        /// <param name="workingDirectoryRoot">The root of the worktree (absolute).</param>
        /// <param name="pathspecs">The literal pathspecs, as the user typed them.</param>
        public static IReadOnlyList<string> ResolveToGitPaths(
            string pathspecBaseDirectory,
            string workingDirectoryRoot,
            IEnumerable<string> pathspecs)
        {
            ArgumentNullException.ThrowIfNull(workingDirectoryRoot);

            List<string> resolved = new List<string>();
            if (pathspecs == null)
            {
                return resolved;
            }

            string rootAbsolute = TrimTrailingSeparators(ToForwardSlashes(workingDirectoryRoot));
            string baseAbsolute = TrimTrailingSeparators(ToForwardSlashes(pathspecBaseDirectory ?? string.Empty));

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string rawPathspec in pathspecs)
            {
                if (TryResolveOne(rawPathspec, rootAbsolute, baseAbsolute, out string gitPath) &&
                    seen.Add(gitPath))
                {
                    resolved.Add(gitPath);
                }
            }

            return resolved;
        }

        /// <summary>
        /// Return true when a path is a repo-relative git-form path that a cone can contain and
        /// that stays inside the worktree on Windows.
        /// </summary>
        /// <remarks>
        /// The path uses forward slashes, has no leading slash, and has an optional trailing
        /// slash for a folder. Its directory part (the folder, or the parent of a file) must
        /// pass <see cref="ConePatternSet.IsValidDirectory"/> and must not contain a wildcard.
        /// No segment can be empty, <c>.</c>, <c>..</c>, or <c>.git</c>, end with a dot or a
        /// space, or be a Windows device name such as <c>CON</c> or <c>COM1</c>. Git for Windows
        /// does not check out such paths. The path must not contain a backslash, a colon, a
        /// control character, or an unpaired surrogate.
        /// </remarks>
        public static bool IsValidGitPath(string path)
        {
            if (string.IsNullOrEmpty(path) || path[0] == GVFSConstants.GitPathSeparator)
            {
                return false;
            }

            for (int i = 0; i < path.Length; i++)
            {
                char character = path[i];
                if (character < ' ' || character == '\\' || character == ':')
                {
                    return false;
                }

                if (char.IsHighSurrogate(character) && i + 1 < path.Length && char.IsLowSurrogate(path[i + 1]))
                {
                    i++;
                }
                else if (char.IsSurrogate(character))
                {
                    return false;
                }
            }

            bool isFolder = path[path.Length - 1] == GVFSConstants.GitPathSeparator;
            string trimmed = isFolder ? path.Substring(0, path.Length - 1) : path;
            foreach (string segment in trimmed.Split(GVFSConstants.GitPathSeparator))
            {
                if (!IsValidSegment(segment))
                {
                    return false;
                }
            }

            int lastSeparator = trimmed.LastIndexOf(GVFSConstants.GitPathSeparator);
            string directory = isFolder ? trimmed : (lastSeparator < 0 ? string.Empty : trimmed.Substring(0, lastSeparator));
            return directory.Length == 0 ||
                (directory.IndexOfAny(WildcardCharacters) < 0 && ConePatternSet.IsValidDirectory(directory));
        }

        private static bool IsValidSegment(string segment)
        {
            if (segment.Length == 0 ||
                segment == "." ||
                segment == ".." ||
                segment.Equals(GVFSConstants.DotGit.Root, StringComparison.OrdinalIgnoreCase) ||
                segment[segment.Length - 1] == '.' ||
                segment[segment.Length - 1] == ' ')
            {
                return false;
            }

            // Windows opens a device for a reserved name, with or without an extension.
            int dot = segment.IndexOf('.');
            string name = (dot < 0 ? segment : segment.Substring(0, dot)).TrimEnd(' ');
            foreach (string reserved in ReservedDeviceNames)
            {
                if (name.Equals(reserved, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            if (name.Length == 4 &&
                (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                name[3] >= '1' && name[3] <= '9')
            {
                return false;
            }

            return true;
        }
        private static bool TryResolveOne(string rawPathspec, string rootAbsolute, string baseAbsolute, out string gitPath)
        {
            gitPath = null;

            // A leading ':' starts a magic pathspec (:/, :(glob), :!, ...). Git expands the
            // index for every magic pathspec, so no cone entry can help.
            if (string.IsNullOrEmpty(rawPathspec) || rawPathspec[0] == ':')
            {
                return false;
            }

            string normalized = ToForwardSlashes(rawPathspec);
            bool isFolder = normalized.EndsWith(GVFSConstants.GitPathSeparatorString, StringComparison.Ordinal);

            string absolute;
            if (IsAbsolute(normalized))
            {
                absolute = normalized;
            }
            else
            {
                absolute = baseAbsolute.Length == 0
                    ? normalized
                    : baseAbsolute + GVFSConstants.GitPathSeparatorString + normalized;
            }

            if (!TryCanonicalize(absolute, out string canonical) ||
                !TryMakeRepoRelative(canonical, rootAbsolute, out string relative))
            {
                return false;
            }

            // An empty result is the worktree root. The cone always includes the files at the
            // root, and a recursive root entry is not a cone entry.
            if (relative.Length == 0 || HasWildcardInDirectorySegment(relative, isFolder))
            {
                return false;
            }

            string candidate = isFolder ? relative + GVFSConstants.GitPathSeparatorString : relative;
            if (!IsValidGitPath(candidate))
            {
                return false;
            }

            gitPath = candidate;
            return true;
        }

        private static bool HasWildcardInDirectorySegment(string relative, bool isFolder)
        {
            int directoryLength = isFolder ? relative.Length : relative.LastIndexOf(GVFSConstants.GitPathSeparator);
            return directoryLength > 0 && relative.IndexOfAny(WildcardCharacters, 0, directoryLength) >= 0;
        }

        private static bool TryCanonicalize(string path, out string canonical)
        {
            canonical = null;

            string[] parts = path.Split(GVFSConstants.GitPathSeparator);
            List<string> segments = new List<string>(parts.Length);
            foreach (string part in parts)
            {
                if (part.Length == 0 || part == ".")
                {
                    continue;
                }

                if (part == "..")
                {
                    // A ".." that goes above the start of a relative path, or above a drive,
                    // cannot map to a path in the worktree.
                    if (segments.Count == 0 || IsDriveSegment(segments[segments.Count - 1]))
                    {
                        return false;
                    }

                    segments.RemoveAt(segments.Count - 1);
                    continue;
                }

                segments.Add(part);
            }

            canonical = string.Join(GVFSConstants.GitPathSeparatorString, segments);
            return true;
        }

        private static bool TryMakeRepoRelative(string canonicalAbsolute, string rootAbsolute, out string relative)
        {
            relative = null;

            if (rootAbsolute.Length == 0)
            {
                relative = canonicalAbsolute;
                return true;
            }

            // Windows paths are not case-sensitive, so compare the root without case. Keep the
            // caller's case in the result, because the cone compares it with index entries.
            if (canonicalAbsolute.Length == rootAbsolute.Length)
            {
                if (canonicalAbsolute.Equals(rootAbsolute, StringComparison.OrdinalIgnoreCase))
                {
                    relative = string.Empty;
                    return true;
                }

                return false;
            }

            if (canonicalAbsolute.Length > rootAbsolute.Length &&
                canonicalAbsolute[rootAbsolute.Length] == GVFSConstants.GitPathSeparator &&
                canonicalAbsolute.StartsWith(rootAbsolute, StringComparison.OrdinalIgnoreCase))
            {
                relative = canonicalAbsolute.Substring(rootAbsolute.Length + 1);
                return true;
            }

            return false;
        }

        private static bool IsAbsolute(string forwardSlashedPath)
        {
            // A drive-rooted path (C:/...) or a path with a leading separator (UNC or rooted).
            // A drive-relative "C:relative" path keeps its colon and IsValidGitPath rejects it.
            if (forwardSlashedPath.StartsWith(GVFSConstants.GitPathSeparatorString, StringComparison.Ordinal))
            {
                return true;
            }

            return forwardSlashedPath.Length >= 3 &&
                IsDriveSegment(forwardSlashedPath.Substring(0, 2)) &&
                forwardSlashedPath[2] == GVFSConstants.GitPathSeparator;
        }

        private static bool IsDriveSegment(string segment)
        {
            return segment.Length == 2 &&
                char.IsLetter(segment[0]) &&
                segment[1] == ':';
        }

        private static string ToForwardSlashes(string path)
        {
            return path.Replace('\\', GVFSConstants.GitPathSeparator);
        }

        private static string TrimTrailingSeparators(string path)
        {
            return path.TrimEnd(GVFSConstants.GitPathSeparator);
        }
    }
}
