using GVFS.Common;
using GVFS.Common.Tracing;
using GVFS.Platform.Windows;
using GVFS.Service.Handlers;
using System;
using System.Diagnostics;

namespace GVFS.Service
{
    public class GVFSMountProcess : IRepoMounter
    {
        private readonly ITracer tracer;

        public GVFSMountProcess(ITracer tracer)
        {
            this.tracer = tracer;
        }

        public bool MountRepository(string repoRoot, int sessionId)
        {
            if (!ProjFSFilter.IsServiceRunning(this.tracer))
            {
                string error;
                if (!EnableAndAttachProjFSHandler.TryEnablePrjFlt(this.tracer, out error))
                {
                    this.tracer.RelatedError($"{nameof(this.MountRepository)}: Could not enable PrjFlt: {error}");
                    return false;
                }
            }

            using (CurrentUser currentUser = new CurrentUser(this.tracer, sessionId))
            {
                int mountProcessId;
                if (!this.TryCallGVFSMount(repoRoot, currentUser, out mountProcessId))
                {
                    this.tracer.RelatedError($"{nameof(this.MountRepository)}: Unable to start the GVFS.exe process.");
                    return false;
                }

                string errorMessage;
                string pipeName = GVFSPlatform.Instance.GetNamedPipeName(repoRoot);
                string worktreeError;
                GVFSEnlistment.WorktreeInfo wtInfo = GVFSEnlistment.TryGetWorktreeInfo(repoRoot, out worktreeError);
                if (worktreeError != null)
                {
                    this.tracer.RelatedError($"Failed to check worktree status for '{repoRoot}': {worktreeError}");
                    return false;
                }

                if (wtInfo?.SharedGitDir != null)
                {
                    string enlistmentRoot = wtInfo.GetEnlistmentRoot();
                    if (enlistmentRoot != null)
                    {
                        pipeName = GVFSPlatform.Instance.GetNamedPipeName(enlistmentRoot) + wtInfo.PipeSuffix;
                    }
                }

                // Track the spawned mount process so the wait short-circuits
                // when it dies early — e.g. on argument-parsing failures that
                // exit before any log file is created. Without this, the
                // service would block for the full 60-second pipe timeout
                // with no diagnostic beyond "not responding."
                Process mountProcess = TryGetProcessById(this.tracer, mountProcessId);
                Func<GVFSEnlistment.MountProcessSnapshot> snapshot =
                    mountProcess == null
                        ? (Func<GVFSEnlistment.MountProcessSnapshot>)null
                        : () => SnapshotMountProcess(mountProcess, mountProcessId);

                try
                {
                    if (!GVFSEnlistment.WaitUntilMounted(this.tracer, pipeName, repoRoot, unattended: false, snapshot, out errorMessage))
                    {
                        this.tracer.RelatedError(errorMessage);
                        return false;
                    }
                }
                finally
                {
                    mountProcess?.Dispose();
                }
            }

            return true;
        }

        private static Process TryGetProcessById(ITracer tracer, int processId)
        {
            try
            {
                return Process.GetProcessById(processId);
            }
            catch (ArgumentException)
            {
                // Process already exited between CreateProcessAsUser returning
                // and us looking it up. Wait loop will catch this on first poll.
                return null;
            }
            catch (InvalidOperationException e)
            {
                tracer.RelatedWarning($"{nameof(TryGetProcessById)}: Could not open handle to mount process Id {processId}: {e.Message}");
                return null;
            }
        }

        private static GVFSEnlistment.MountProcessSnapshot SnapshotMountProcess(Process mountProcess, int processId)
        {
            try
            {
                if (mountProcess.HasExited)
                {
                    return new GVFSEnlistment.MountProcessSnapshot(processId, hasExited: true, exitCode: mountProcess.ExitCode);
                }
            }
            catch (InvalidOperationException)
            {
                // No process associated — treat as exited so the caller fails fast.
                return new GVFSEnlistment.MountProcessSnapshot(processId, hasExited: true, exitCode: -1);
            }

            return new GVFSEnlistment.MountProcessSnapshot(processId, hasExited: false, exitCode: 0);
        }

        private bool TryCallGVFSMount(string repoRoot, CurrentUser currentUser, out int processId)
        {
            InternalVerbParameters mountInternal = new InternalVerbParameters(startedByService: true);
            return currentUser.TryRunAs(
                Configuration.Instance.GVFSLocation,
                new[]
                {
                    "mount",
                    repoRoot,
                    "--" + GVFSConstants.VerbParameters.InternalUseOnly,
                    mountInternal.ToJson(),
                },
                out processId);
        }
    }
}

