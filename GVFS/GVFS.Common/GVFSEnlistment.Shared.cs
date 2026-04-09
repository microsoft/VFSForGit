using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security;

namespace GVFS.Common
{
    public partial class GVFSEnlistment
    {
        public static bool IsUnattended(ITracer tracer)
        {
            try
            {
                return Environment.GetEnvironmentVariable(GVFSConstants.UnattendedEnvironmentVariable) == "1";
            }
            catch (SecurityException e)
            {
                if (tracer != null)
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Area", nameof(GVFSEnlistment));
                    metadata.Add("Exception", e.ToString());
                    tracer.RelatedError(metadata, "Unable to read environment variable " + GVFSConstants.UnattendedEnvironmentVariable);
                }

                return false;
            }
        }

        /// <summary>
        /// Returns true if <paramref name="path"/> is equal to or a subdirectory of
        /// <paramref name="directory"/> (case-insensitive). Both paths are
        /// canonicalized with <see cref="Path.GetFullPath(string)"/> to resolve
        /// relative segments (e.g. "/../") before comparison.
        /// </summary>
        public static bool IsPathInsideDirectory(string path, string directory)
        {
            string normalizedPath = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedDirectory = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return normalizedPath.StartsWith(normalizedDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                   normalizedPath.Equals(normalizedDirectory, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Detects if the given directory is a git worktree by checking for
        /// a .git file (not directory) containing "gitdir: path/.git/worktrees/name".
        /// Returns a pipe name suffix like "_WT_NAME" if so, or null if not a worktree.
        /// </summary>
        public static string GetWorktreePipeSuffix(string directory)
        {
            WorktreeInfo info = TryGetWorktreeInfo(directory);
            return info?.PipeSuffix;
        }

        /// <summary>
        /// Detects if the given directory (or any ancestor) is a git worktree.
        /// Walks up from <paramref name="directory"/> looking for a <c>.git</c>
        /// file (not directory) containing a <c>gitdir:</c> pointer. Returns
        /// null if not inside a worktree.
        /// </summary>
        public static WorktreeInfo TryGetWorktreeInfo(string directory)
        {
            return TryGetWorktreeInfo(directory, out _);
        }

        /// <summary>
        /// Detects if the given directory (or any ancestor) is a git worktree.
        /// Walks up from <paramref name="directory"/> looking for a <c>.git</c>
        /// file (not directory) containing a <c>gitdir:</c> pointer. Returns
        /// null if not inside a worktree, with an error message if an I/O
        /// error prevented detection.
        /// </summary>
        public static WorktreeInfo TryGetWorktreeInfo(string directory, out string error)
        {
            error = null;

            if (string.IsNullOrEmpty(directory))
            {
                return null;
            }

            // Canonicalize to an absolute path so walk-up and Path.Combine
            // behave consistently regardless of the caller's CWD.
            string current = Path.GetFullPath(directory);
            while (current != null)
            {
                string dotGitPath = Path.Combine(current, ".git");

                if (Directory.Exists(dotGitPath))
                {
                    // Found a real .git directory — this is a primary worktree, not a linked worktree
                    return null;
                }

                if (File.Exists(dotGitPath))
                {
                    return TryParseWorktreeGitFile(current, dotGitPath, out error);
                }

                string parent = Path.GetDirectoryName(current);
                if (parent == current)
                {
                    break;
                }

                current = parent;
            }

            return null;
        }

        private static WorktreeInfo TryParseWorktreeGitFile(string worktreeRoot, string dotGitPath, out string error)
        {
            error = null;

            try
            {
                string gitdirLine = File.ReadAllText(dotGitPath).Trim();
                if (!gitdirLine.StartsWith(GVFSConstants.DotGit.GitDirPrefix))
                {
                    return null;
                }

                string gitdirPath = gitdirLine.Substring(GVFSConstants.DotGit.GitDirPrefix.Length).Trim();
                gitdirPath = gitdirPath.Replace('/', Path.DirectorySeparatorChar);

                // Resolve relative paths against the worktree directory
                if (!Path.IsPathRooted(gitdirPath))
                {
                    gitdirPath = Path.GetFullPath(Path.Combine(worktreeRoot, gitdirPath));
                }

                string worktreeName = Path.GetFileName(gitdirPath);
                if (string.IsNullOrEmpty(worktreeName))
                {
                    return null;
                }

                // Read commondir to find the shared .git/ directory.
                // All valid worktrees must have a commondir file.
                string commondirFile = Path.Combine(gitdirPath, GVFSConstants.DotGit.CommonDirName);
                if (!File.Exists(commondirFile))
                {
                    return null;
                }

                string commondirContent = File.ReadAllText(commondirFile).Trim();
                string sharedGitDir = Path.GetFullPath(Path.Combine(gitdirPath, commondirContent));

                return new WorktreeInfo
                {
                    Name = worktreeName,
                    WorktreePath = worktreeRoot,
                    WorktreeGitDir = gitdirPath,
                    SharedGitDir = sharedGitDir,
                    PipeSuffix = "_WT_" + worktreeName.ToUpper(),
                };
            }
            catch (IOException e)
            {
                error = e.Message;
                return null;
            }
            catch (UnauthorizedAccessException e)
            {
                error = e.Message;
                return null;
            }
        }

        /// <summary>
        /// Returns the working directory paths of all worktrees registered
        /// under <paramref name="gitDir"/>/worktrees by reading each entry's
        /// gitdir file. The primary worktree is not included.
        /// </summary>
        public static string[] GetKnownWorktreePaths(string gitDir)
        {
            string worktreesDir = Path.Combine(gitDir, "worktrees");
            if (!Directory.Exists(worktreesDir))
            {
                return new string[0];
            }

            List<string> paths = new List<string>();
            foreach (string entry in Directory.GetDirectories(worktreesDir))
            {
                string gitdirFile = Path.Combine(entry, "gitdir");
                if (!File.Exists(gitdirFile))
                {
                    continue;
                }

                try
                {
                    string gitdirContent = File.ReadAllText(gitdirFile).Trim();
                    gitdirContent = gitdirContent.Replace('/', Path.DirectorySeparatorChar);
                    string worktreeDir = Path.GetDirectoryName(gitdirContent);
                    if (!string.IsNullOrEmpty(worktreeDir))
                    {
                        paths.Add(Path.GetFullPath(worktreeDir));
                    }
                }
                catch
                {
                }
            }

            return paths.ToArray();
        }

        public class WorktreeInfo
        {
            public const string EnlistmentRootFileName = "gvfs-enlistment-root";

            public string Name { get; set; }
            public string WorktreePath { get; set; }
            public string WorktreeGitDir { get; set; }
            public string SharedGitDir { get; set; }
            public string PipeSuffix { get; set; }

            /// <summary>
            /// Returns the primary enlistment root, either from a stored
            /// marker file or by deriving it from SharedGitDir.
            /// </summary>
            public string GetEnlistmentRoot()
            {
                // Prefer the explicit marker written during worktree creation
                string markerPath = Path.Combine(this.WorktreeGitDir, EnlistmentRootFileName);
                if (File.Exists(markerPath))
                {
                    string root = File.ReadAllText(markerPath).Trim();
                    if (!string.IsNullOrEmpty(root))
                    {
                        return root;
                    }
                }

                // Fallback: derive from SharedGitDir (assumes <root>/src/.git)
                if (this.SharedGitDir != null)
                {
                    string srcDir = Path.GetDirectoryName(this.SharedGitDir);
                    if (srcDir != null)
                    {
                        return Path.GetDirectoryName(srcDir);
                    }
                }

                return null;
            }
        }
    }
}
