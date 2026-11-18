using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.NamedPipes;
using GVFS.Common.Sparse;
using GVFS.Common.Tracing;
using GVFS.Hooks.HooksPlatform;
using GVFS.Platform.Windows;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GVFS.Hooks
{
    /// <summary>
    /// Automatic sparse-index cone management. See <see cref="ConeManagementHook"/>.
    /// </summary>
    public partial class Program
    {
        private const string GitPrefixEnvironmentVariable = "GIT_PREFIX";

        // A cone file larger than this is not read. The hook then does not widen, and Git
        // expands the index for the command.
        private const long MaxConeFileBytes = 16 * 1024 * 1024;

        // True when the pre-command hook took the GVFS lock for the Git command.
        private static bool commandHoldsGVFSLock;

        // The hooks that sent the widen and the apply of this command, or null.
        private static ConeManagementHook widenHook;
        private static PendingConeChangeHook applyHook;

        // True when a read of the cone file or of the feature flag failed since the last reset.
        private static bool coneStateReadFailed;

        private static ConeRequestOutcome TrySendConeWiden(string[] args)
        {
            try
            {
                widenHook = CreateConeManagementHook();
                return widenHook.TrySendWiden(args);
            }
            catch (Exception e)
            {
                TraceConeDiagnostic($"widen failed: {e.Message}");
                return ConeRequestOutcome.Failed;
            }
        }

        /// <summary>
        /// Before Git reads the index, make sure that the cone includes every working-tree change
        /// that the mount saw. See <see cref="PreCommandConeRequests"/> for when the hook calls this.
        /// </summary>
        private static ConeRequestOutcome TryApplyPendingConeChange(string[] args)
        {
            try
            {
                applyHook = new PendingConeChangeHook(IsConeChangePending, DeleteConeChangePendingFile, () => new NamedPipeClient(enlistmentPipename), TraceConeDiagnostic);
                return applyHook
                    .TryApply(
                        args,
                        commandHoldsGVFSLock,
                        processId => new NamedPipeMessages.LockRequest(
                            processId,
                            GVFSHooksPlatform.IsElevated(),
                            checkAvailabilityOnly: false,
                            GenerateFullCommand(args),
                            GetGitCommandSessionId()));
            }
            catch (Exception e)
            {
                TraceConeDiagnostic($"apply failed: {e.Message}");
                return ConeRequestOutcome.Failed;
            }
        }

        /// <summary>
        /// Before the hook stops a command, wait for a widen or an apply that it stopped waiting
        /// for. The mount can still run Git for it, and that Git holds the index lock. The GVFS
        /// lock of this command is released when the command ends, so the next Git command could
        /// otherwise start while the mount still writes the index.
        /// </summary>
        private static void WaitForAbandonedConeRequests()
        {
            Task[] abandoned = new[] { widenHook?.AbandonedRequest, applyHook?.AbandonedRequest }
                .Where(task => task != null && !task.IsCompleted)
                .ToArray();
            if (abandoned.Length == 0)
            {
                return;
            }

            Console.Error.WriteLine("Waiting for GVFS to finish the sparse index change...");
            try
            {
                if (!Task.WaitAll(abandoned, PendingConeChangeHook.ApplyCompletionWaitMs))
                {
                    TraceConeDiagnostic("cone request still running; stopping the command");
                }
            }
            catch (AggregateException e)
            {
                TraceConeDiagnostic($"cone request failed: {e.InnerException?.Message}");
            }
        }

        /// <summary>
        /// Return false only when it is known that GVFS does not manage a sparse-index cone for
        /// this worktree. When the hook cannot read the cone file or the feature flag, it cannot
        /// tell, and a command that writes the index must stop.
        /// </summary>
        private static bool MayManageSparseIndexCone()
        {
            coneStateReadFailed = false;
            return IsSparseIndexConeManaged() || coneStateReadFailed;
        }

        /// <summary>
        /// Return true when the mount's pending file exists (see
        /// <see cref="GVFSConstants.DotGVFS.ConeChangePending"/>), or when the hook cannot check it.
        /// When nothing is pending, this is the only cost that cone management adds to a command
        /// that names no paths.
        /// </summary>
        private static bool IsConeChangePending()
        {
            return PendingConeChangeHook.IsPendingFilePresentOrUnknown(GetConeChangePendingPath, File.GetAttributes, TraceConeDiagnostic);
        }

        private static void DeleteConeChangePendingFile()
        {
            try
            {
                File.Delete(GetConeChangePendingPath());
            }
            catch (Exception e)
            {
                TraceConeDiagnostic($"cannot delete the pending cone change file: {e.Message}");
            }
        }

        private static string GetConeChangePendingPath()
        {
            string dotGVFSRoot = currentWorktreeInfo != null
                ? Path.Combine(currentWorktreeInfo.WorktreeGitDir, WindowsPlatform.DotGVFSRoot)
                : Path.Combine(enlistmentRoot, WindowsPlatform.DotGVFSRoot);
            return Path.Combine(dotGVFSRoot, GVFSConstants.DotGVFS.ConeChangePending);
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

        /// <summary>
        /// Return true when GVFS manages a sparse-index cone for this worktree. See
        /// <see cref="ConeManagementHook.IsSparseIndexConeManaged"/>.
        /// </summary>
        private static bool IsSparseIndexConeManaged()
        {
            try
            {
                return CreateConeManagementHook().IsSparseIndexConeManaged();
            }
            catch (Exception e)
            {
                coneStateReadFailed = true;
                TraceConeDiagnostic($"cannot check for a sparse-index cone: {e.Message}");
                return false;
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

                    ignoreCase = repo.GetConfigBool(GitConfigSetting.CoreIgnoreCaseName) ?? false;
                    return true;
                }
            }
            catch (Exception e)
            {
                coneStateReadFailed = true;
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
                    coneStateReadFailed = true;
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
                            coneStateReadFailed = true;
                            TraceConeDiagnostic($"cone file not read: {sparseCheckoutPath} is larger than {MaxConeFileBytes} bytes");
                            return null;
                        }
                    }

                    return ConeCoverage.DecodeConeFile(content.ToArray());
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                coneStateReadFailed = true;
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
