using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.NamedPipes;
using GVFS.Common.Sparse;
using GVFS.Common.Tracing;
using GVFS.Hooks.HooksPlatform;
using System;
using System.IO;

namespace GVFS.Hooks
{
    /// <summary>
    /// Automatic sparse-index cone management. See <see cref="ConeManagementHook"/>.
    /// </summary>
    public partial class Program
    {
        private const string CoreIgnoreCaseConfig = "core.ignorecase";
        private const string GitPrefixEnvironmentVariable = "GIT_PREFIX";

        // A cone file larger than this is not read. The hook then does not widen, and Git
        // expands the index for the command.
        private const long MaxConeFileBytes = 16 * 1024 * 1024;

        private static void TrySendConeWiden(string[] args)
        {
            try
            {
                CreateConeManagementHook().TrySendWiden(args);
            }
            catch (Exception e)
            {
                TraceConeDiagnostic($"widen failed: {e.Message}");
            }
        }

        private static void TrySendConeNarrow(string[] args)
        {
            try
            {
                CreateConeManagementHook().TrySendNarrow(args);
            }
            catch (Exception e)
            {
                TraceConeDiagnostic($"narrow failed: {e.Message}");
            }
        }

        private static ConeManagementHook CreateConeManagementHook()
        {
            return new ConeManagementHook(
                ReadAutoSparseIndexConfig,
                ReadConeFile,
                () => new NamedPipeClient(enlistmentPipename),
                normalizedCurrentDirectory,
                GetConeWorkingDirectoryRoot,
                Environment.GetEnvironmentVariable(GitPrefixEnvironmentVariable),
                TraceConeDiagnostic);
        }

        private static bool ReadAutoSparseIndexConfig(out bool ignoreCase)
        {
            ignoreCase = false;
            try
            {
                using (LibGit2Repo repo = new LibGit2Repo(NullTracer.Instance, normalizedCurrentDirectory, GitConfigReadRetry.Delay, ConfigReadMaxAttempts))
                {
                    // GetConfigBool throws for an unreadable or non-boolean value, so that the
                    // catch below can trace it.
                    if (!(repo.GetConfigBool(GVFSConstants.GitConfig.AutoSparseIndex) ?? GVFSConstants.GitConfig.AutoSparseIndexDefault))
                    {
                        return false;
                    }

                    ignoreCase = repo.GetConfigBool(CoreIgnoreCaseConfig) ?? false;
                    return true;
                }
            }
            catch (Exception e)
            {
                TraceConeDiagnostic($"cannot read {GVFSConstants.GitConfig.AutoSparseIndex}: {e.Message}");
                return GVFSConstants.GitConfig.AutoSparseIndexDefault;
            }
        }

        /// <summary>
        /// Return the worktree root in the same form as <see cref="normalizedCurrentDirectory"/>,
        /// so that the pathspec resolver can compare the two.
        /// </summary>
        private static string GetConeWorkingDirectoryRoot()
        {
            string root = currentWorktreeInfo != null
                ? currentWorktreeInfo.WorktreePath
                : Path.Combine(enlistmentRoot, GVFSConstants.WorkingDirectoryRootName);

            return GVFSHooksPlatform.TryGetNormalizedPath(root, out string normalizedRoot, out _) ? normalizedRoot : root;
        }

        private static string ReadConeFile()
        {
            string sparseCheckoutPath = SparseCheckoutPathResolver.GetSparseCheckoutFilePath(
                isWorktree: currentWorktreeInfo != null,
                dotGitRoot: Path.Combine(enlistmentRoot, GVFSConstants.WorkingDirectoryRootName, GVFSConstants.DotGit.Root),
                worktreeGitDir: currentWorktreeInfo?.WorktreeGitDir);

            try
            {
                // Open the file once, and read at most one byte more than the limit, so that a
                // file that changes after the attribute check cannot make the read unbounded.
                FileInfo file = new FileInfo(sparseCheckoutPath);
                if (!file.Exists)
                {
                    return null;
                }

                if ((file.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                {
                    TraceConeDiagnostic($"cone file not read: {sparseCheckoutPath} is not a regular file");
                    return null;
                }

                using (FileStream stream = new FileStream(sparseCheckoutPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (MemoryStream content = new MemoryStream())
                {
                    byte[] buffer = new byte[64 * 1024];
                    int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        content.Write(buffer, 0, read);
                        if (content.Length > MaxConeFileBytes)
                        {
                            TraceConeDiagnostic($"cone file not read: {sparseCheckoutPath} is larger than {MaxConeFileBytes} bytes");
                            return null;
                        }
                    }

                    return ConeCoverage.DecodeConeFile(content.ToArray());
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                TraceConeDiagnostic($"cone file not read: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Write a cone-management diagnostic to stderr, but only when GIT_TRACE sends Git's own
        /// trace output to stderr (1, 2, or true). Then a skipped request is distinct from a
        /// failed one, and the hook writes nothing in normal use.
        /// </summary>
        private static void TraceConeDiagnostic(string message)
        {
            string gitTrace = Environment.GetEnvironmentVariable("GIT_TRACE");
            if (!string.Equals(gitTrace, "1", StringComparison.Ordinal) &&
                !string.Equals(gitTrace, "2", StringComparison.Ordinal) &&
                !string.Equals(gitTrace, "true", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                Console.Error.WriteLine($"gvfs cone-management: {message}");
            }
            catch (IOException)
            {
            }
        }
    }
}
