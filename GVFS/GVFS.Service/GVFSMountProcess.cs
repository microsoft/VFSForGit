using GVFS.Common;
using GVFS.Common.Tracing;
using GVFS.Platform.Windows;
using GVFS.Service.Handlers;
using Microsoft.Win32.SafeHandles;
using System;

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
                SafeProcessHandle mountHandle;
                int mountProcessId;
                if (!this.TryCallGVFSMount(repoRoot, currentUser, out mountHandle, out mountProcessId))
                {
                    this.tracer.RelatedError($"{nameof(this.MountRepository)}: Unable to start the GVFS.exe process.");
                    return false;
                }

                // Always own the handle for the rest of this call so the
                // kernel keeps the process object alive while we poll it.
                using (mountHandle)
                {
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
                    //
                    // We use the SafeProcessHandle returned by TryRunAs rather
                    // than Process.GetProcessById(pid) so we cannot race against
                    // the child exiting between CreateProcessAsUser and the
                    // lookup, and cannot alias a reused PID.
                    SafeProcessHandle handle = mountHandle;
                    Func<GVFSEnlistment.MountProcessSnapshot> snapshot =
                        () => SnapshotMountProcess(handle, mountProcessId);

                    if (!GVFSEnlistment.WaitUntilMounted(this.tracer, pipeName, repoRoot, unattended: false, snapshot, out errorMessage))
                    {
                        this.tracer.RelatedError(errorMessage);
                        return false;
                    }
                }
            }

            return true;
        }

        private static GVFSEnlistment.MountProcessSnapshot SnapshotMountProcess(SafeProcessHandle handle, int processId)
        {
            if (!ProcessHandleHelper.HasExited(handle))
            {
                return new GVFSEnlistment.MountProcessSnapshot(processId, hasExited: false, exitCode: 0);
            }

            int exitCode;
            if (!ProcessHandleHelper.TryGetExitCode(handle, out exitCode))
            {
                exitCode = -1;
            }

            return new GVFSEnlistment.MountProcessSnapshot(processId, hasExited: true, exitCode: exitCode);
        }

        private bool TryCallGVFSMount(string repoRoot, CurrentUser currentUser, out SafeProcessHandle processHandle, out int processId)
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
                out processHandle,
                out processId);
        }
    }
}


