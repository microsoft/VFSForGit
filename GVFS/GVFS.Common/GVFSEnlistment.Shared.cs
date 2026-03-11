using GVFS.Common.Tracing;
using System;
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
        /// Detects if the given directory is a git worktree. If so, returns
        /// a WorktreeInfo with the worktree name, git dir path, and shared
        /// git dir path. Returns null if not a worktree.
        /// </summary>
        public static WorktreeInfo TryGetWorktreeInfo(string directory)
        {
            string dotGitPath = Path.Combine(directory, ".git");

            if (!File.Exists(dotGitPath) || Directory.Exists(dotGitPath))
            {
                return null;
            }

            try
            {
                string gitdirLine = File.ReadAllText(dotGitPath).Trim();
                if (!gitdirLine.StartsWith("gitdir: "))
                {
                    return null;
                }

                string gitdirPath = gitdirLine.Substring("gitdir: ".Length).Trim();
                gitdirPath = gitdirPath.Replace('/', Path.DirectorySeparatorChar);

                // Resolve relative paths against the worktree directory
                if (!Path.IsPathRooted(gitdirPath))
                {
                    gitdirPath = Path.GetFullPath(Path.Combine(directory, gitdirPath));
                }

                string worktreeName = Path.GetFileName(gitdirPath);
                if (string.IsNullOrEmpty(worktreeName))
                {
                    return null;
                }

                // Read commondir to find the shared .git/ directory
                // commondir file contains a relative path like "../../.."
                string commondirFile = Path.Combine(gitdirPath, "commondir");
                string sharedGitDir = null;
                if (File.Exists(commondirFile))
                {
                    string commondirContent = File.ReadAllText(commondirFile).Trim();
                    sharedGitDir = Path.GetFullPath(Path.Combine(gitdirPath, commondirContent));
                }

                return new WorktreeInfo
                {
                    Name = worktreeName,
                    WorktreePath = directory,
                    WorktreeGitDir = gitdirPath,
                    SharedGitDir = sharedGitDir,
                    PipeSuffix = "_WT_" + worktreeName.ToUpper(),
                };
            }
            catch
            {
                return null;
            }
        }

        public class WorktreeInfo
        {
            public string Name { get; set; }
            public string WorktreePath { get; set; }
            public string WorktreeGitDir { get; set; }
            public string SharedGitDir { get; set; }
            public string PipeSuffix { get; set; }
        }
    }
}
