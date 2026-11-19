using GVFS.Common.NamedPipes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.Hooks
{
    /// <summary>
    /// Asks the mount to apply a pending sparse-index cone change before Git reads the index.
    /// </summary>
    /// <remarks>
    /// When a working-tree change needs a directory that the index does not expand, the mount
    /// creates a pending file. The file system callback that sees the change does not wait for a
    /// Git command. The hook checks that file with one file-system query, so a command pays
    /// nothing more when no change is pending.
    /// <para>
    /// Only the Git process that holds the GVFS lock can apply a cone change. A command that
    /// takes the lock holds it when this hook runs. A command that reads the index without the
    /// lock (for example <c>git diff</c>) takes the lock for the apply, and releases it before
    /// Git runs. If another Git command holds the lock, the hook does not wait, because that
    /// command can be the parent of this one. The reader can then miss a change that is not
    /// applied yet, as it can miss any change that a concurrent command makes.
    /// </para>
    /// <para>
    /// As for the other cone requests, every failure lets Git run. When the mount replies that
    /// the feature is off, or does not know the request, the hook deletes the pending file,
    /// because that mount never applies the change.
    /// </para>
    /// </remarks>
    internal sealed class PendingConeChangeHook
    {
        // How long the hook waits before it asks for the GVFS lock again.
        private const int LockRetryDelayMs = 25;

        // The release must reach the mount, or the lock stays with the Git process until it ends.
        private const int ReleaseConnectTimeoutMs = 5000;

        // After a timeout, the hook of a command without the lock still waits this long for the
        // mount to finish, so that it does not release the lock while the mount changes the index.
        private const int ApplyCompletionWaitMs = 60000;

        // Built-in commands that read the index or the working tree through the index and do not
        // take the GVFS lock. "status" is here for "--no-optional-locks", which only checks the
        // lock. A "git-" prefix means an alias or a program, which is not in this list: the Git
        // commands that it runs have their own hooks.
        private static readonly HashSet<string> UnlockedIndexReaders = new HashSet<string>(StringComparer.Ordinal)
        {
            "annotate",
            "blame",
            "cat-file",
            "check-attr",
            "check-ignore",
            "diff",
            "diff-files",
            "diff-index",
            "difftool",
            "ls-files",
            "rev-parse",
            "show",
            "status",
        };

        private readonly Func<bool> isPending;
        private readonly Action deletePendingFile;
        private readonly Func<NamedPipeClient> createPipeClient;
        private readonly Action<string> trace;

        /// <param name="isPending">Returns true when the mount's pending file exists.</param>
        /// <param name="deletePendingFile">Deletes the mount's pending file.</param>
        /// <param name="createPipeClient">Creates an unconnected client for the mount pipe.</param>
        /// <param name="trace">Writes a diagnostic message.</param>
        public PendingConeChangeHook(Func<bool> isPending, Action deletePendingFile, Func<NamedPipeClient> createPipeClient, Action<string> trace)
        {
            this.isPending = isPending ?? throw new ArgumentNullException(nameof(isPending));
            this.deletePendingFile = deletePendingFile ?? throw new ArgumentNullException(nameof(deletePendingFile));
            this.createPipeClient = createPipeClient ?? throw new ArgumentNullException(nameof(createPipeClient));
            this.trace = trace ?? (message => { });
        }

        /// <summary>
        /// If a cone change is pending, ask the mount to apply it, and wait for the reply. Never throws.
        /// </summary>
        /// <remarks>
        /// The hook connects and sends the request within <paramref name="sendBudgetMs"/>. If it
        /// cannot, it does not send the request, and Git runs. After it sent the request, it
        /// waits for the reply up to <paramref name="replyCeilingMs"/>: the mount applies the
        /// cone with a Git process that holds the index lock, so Git must not run before that
        /// process ends.
        /// </remarks>
        /// <param name="args">The hook arguments.</param>
        /// <param name="holdsLock">True when this hook took the GVFS lock for the Git command.</param>
        /// <param name="createLockRequest">Creates the GVFS lock request for a Git process id.</param>
        /// <param name="replyCeilingMs">The longest wait for the reply after the request was sent.</param>
        /// <param name="sendBudgetMs">The longest time to connect and send the request.</param>
        /// <param name="lockWaitBudgetMs">The longest wait for the GVFS lock.</param>
        public ConeRequestOutcome TryApply(
            string[] args,
            bool holdsLock,
            Func<int, NamedPipeMessages.LockRequest> createLockRequest,
            int replyCeilingMs = NamedPipeMessages.ConeManagement.WidenReplyCeilingMs,
            int sendBudgetMs = NamedPipeMessages.ConeManagement.WidenWaitBudgetMs,
            int lockWaitBudgetMs = NamedPipeMessages.ConeManagement.WidenWaitBudgetMs)
        {
            ConeRequestOutcome outcome;
            try
            {
                outcome = this.Apply(args, holdsLock, createLockRequest, sendBudgetMs, replyCeilingMs, lockWaitBudgetMs);
            }
            catch (Exception e)
            {
                this.trace($"apply failed: {e.Message}");
                return ConeRequestOutcome.Failed;
            }

            switch (outcome)
            {
                case ConeRequestOutcome.NothingPending:
                case ConeRequestOutcome.NotAnIndexReader:
                case ConeRequestOutcome.Applied:
                    break;

                default:
                    this.trace($"apply not applied: {outcome}");
                    break;
            }

            return outcome;
        }

        internal static bool IsUnlockedIndexReader(string[] args)
        {
            return args != null && args.Length > 1 && args[1] != null && UnlockedIndexReaders.Contains(args[1]);
        }

        private ConeRequestOutcome Apply(string[] args, bool holdsLock, Func<int, NamedPipeMessages.LockRequest> createLockRequest, int sendBudgetMs, int replyCeilingMs, int lockWaitBudgetMs)
        {
            if (!this.isPending())
            {
                return ConeRequestOutcome.NothingPending;
            }

            string commandId = ConeManagementHook.GetCommandId(args);
            if (holdsLock)
            {
                return this.WaitForApply(this.StartApply(commandId), sendBudgetMs, replyCeilingMs);
            }

            if (!IsUnlockedIndexReader(args))
            {
                return ConeRequestOutcome.NotAnIndexReader;
            }

            // Use the same process id as the apply request. The mount accepts the apply only
            // from the process that holds the lock.
            if (!int.TryParse(commandId, out int processId) || processId <= 0)
            {
                return ConeRequestOutcome.Failed;
            }

            NamedPipeMessages.LockRequest lockRequest = createLockRequest(processId);
            if (!this.TryAcquireLock(lockRequest, lockWaitBudgetMs))
            {
                return ConeRequestOutcome.LockUnavailable;
            }

            ApplyRequest apply = null;
            try
            {
                apply = this.StartApply(commandId);
                return this.WaitForApply(apply, sendBudgetMs, replyCeilingMs);
            }
            finally
            {
                // The mount can still run Git for a request that the hook sent. Another command
                // must not get the lock until it is done, so wait for it, but not forever.
                if (apply != null && apply.WasSent && !apply.Reply.IsCompleted && !apply.Reply.Wait(ApplyCompletionWaitMs))
                {
                    this.trace("apply still running; releasing the GVFS lock");
                }

                this.ReleaseLock(lockRequest);
            }
        }

        private bool TryAcquireLock(NamedPipeMessages.LockRequest lockRequest, int lockWaitBudgetMs)
        {
            NamedPipeMessages.Message request = lockRequest.CreateMessage(NamedPipeMessages.AcquireLock.AcquireRequest);
            Stopwatch stopwatch = Stopwatch.StartNew();
            using (NamedPipeClient pipeClient = this.createPipeClient())
            {
                if (!pipeClient.Connect(NamedPipeMessages.ConeManagement.ConnectTimeoutMs, out _))
                {
                    return false;
                }

                while (true)
                {
                    pipeClient.SendRequest(request);
                    NamedPipeMessages.Message reply = pipeClient.ReadResponse();
                    if (reply == null)
                    {
                        return false;
                    }

                    NamedPipeMessages.AcquireLock.Response response = new NamedPipeMessages.AcquireLock.Response(reply);
                    switch (response.Result)
                    {
                        case NamedPipeMessages.AcquireLock.AcceptResult:
                            return true;

                        case NamedPipeMessages.AcquireLock.DenyGVFSResult:
                            // The mount is still processing working-tree changes, which can be
                            // the change that this command must see. Wait for it.
                            if (stopwatch.ElapsedMilliseconds + LockRetryDelayMs > lockWaitBudgetMs)
                            {
                                this.trace($"apply skipped: GVFS lock result {response.Result}");
                                return false;
                            }

                            Thread.Sleep(LockRetryDelayMs);
                            break;

                        default:
                            this.trace($"apply skipped: GVFS lock result {response.Result}");
                            return false;
                    }
                }
            }
        }

        private void ReleaseLock(NamedPipeMessages.LockRequest lockRequest)
        {
            using (NamedPipeClient pipeClient = this.createPipeClient())
            {
                if (!pipeClient.Connect(ReleaseConnectTimeoutMs, out Exception error))
                {
                    this.trace($"apply could not release the GVFS lock: {error?.Message}");
                    return;
                }

                pipeClient.SendRequest(lockRequest.CreateMessage(NamedPipeMessages.ReleaseLock.Request));
                NamedPipeMessages.Message reply = pipeClient.ReadResponse();
                if (reply?.Header != NamedPipeMessages.ReleaseLock.SuccessResult)
                {
                    this.trace($"apply could not release the GVFS lock: {reply?.Header}");
                }
            }
        }

        private ApplyRequest StartApply(string commandId)
        {
            string body = new NamedPipeMessages.ConeManagement.ApplyParameters(commandId).ToBody();
            ApplyRequest apply = new ApplyRequest();
            apply.Reply = Task.Run(() =>
            {
                using (NamedPipeClient pipeClient = this.createPipeClient())
                {
                    if (!pipeClient.Connect(NamedPipeMessages.ConeManagement.ConnectTimeoutMs, out _))
                    {
                        return ConeRequestOutcome.MountUnavailable;
                    }

                    // After the send budget ends, the hook lets Git run. A request sent after
                    // that could make the mount run Git while the user's Git runs.
                    if (!apply.TryMarkSent())
                    {
                        return ConeRequestOutcome.TimedOut;
                    }

                    pipeClient.SendRequest(new NamedPipeMessages.Message(NamedPipeMessages.ConeManagement.ApplyRequest, body));
                    string result = pipeClient.ReadResponse()?.Header;
                    switch (result)
                    {
                        case NamedPipeMessages.ConeManagement.SuccessResult:
                            return ConeRequestOutcome.Applied;

                        case NamedPipeMessages.ConeManagement.NotEnabledResult:
                        case NamedPipeMessages.UnknownRequest:
                            // This mount never applies the change, so the file is stale.
                            this.deletePendingFile();
                            return result == NamedPipeMessages.UnknownRequest ? ConeRequestOutcome.Rejected : ConeRequestOutcome.FeatureDisabled;

                        default:
                            return ConeRequestOutcome.Rejected;
                    }
                }
            });

            return apply;
        }

        private ConeRequestOutcome WaitForApply(ApplyRequest apply, int sendBudgetMs, int replyCeilingMs)
        {
            if (Task.WaitAny(new Task[] { apply.Reply, apply.Sending }, sendBudgetMs) < 0 && apply.TryAbandon())
            {
                // The request was not sent, and now it never is.
                return ConeRequestOutcome.TimedOut;
            }

            // The request was sent, or the task ended. Git must not run while the mount can
            // still run Git for the request, up to the ceiling.
            return apply.Reply.Wait(replyCeilingMs) ? apply.Reply.Result : ConeRequestOutcome.ReplyTimedOut;
        }

        /// <summary>One <c>ConeApply</c> request: the send gate and the task that reads the reply.</summary>
        private sealed class ApplyRequest
        {
            private readonly object gate = new object();
            private readonly TaskCompletionSource<bool> sending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private bool abandoned;
            private bool sent;

            public Task<ConeRequestOutcome> Reply { get; set; }

            public Task Sending => this.sending.Task;

            public bool WasSent
            {
                get
                {
                    lock (this.gate)
                    {
                        return this.sent;
                    }
                }
            }

            /// <summary>Return false when the hook gave up before the send.</summary>
            public bool TryMarkSent()
            {
                lock (this.gate)
                {
                    if (this.abandoned)
                    {
                        return false;
                    }

                    this.sent = true;
                }

                this.sending.TrySetResult(true);
                return true;
            }

            /// <summary>Return false when the request was already sent.</summary>
            public bool TryAbandon()
            {
                lock (this.gate)
                {
                    if (this.sent)
                    {
                        return false;
                    }

                    this.abandoned = true;
                    return true;
                }
            }
        }
    }
}
