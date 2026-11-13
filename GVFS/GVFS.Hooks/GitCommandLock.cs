using GVFS.Common.NamedPipes;
using System;

namespace GVFS.Hooks
{
    internal static class GitCommandLock
    {
        private const string CheckMount = "Run 'gvfs status' to check the mount.";

        /// <summary>
        /// Returns true if Git runs "status" without locks, so the hook only checks that the GVFS
        /// lock is free. "--no-optional-locks" sets GIT_OPTIONAL_LOCKS to a false value. A raw
        /// "git-status" is an external program, so it never qualifies.
        /// </summary>
        public static bool IsAvailabilityOnly(string[] args, Func<string, string> getEnvironmentVariable)
        {
            return string.Equals(args[1], "status", StringComparison.Ordinal) &&
                (Array.Exists(args, arg => arg.Equals("--no-lock-index", StringComparison.OrdinalIgnoreCase)) ||
                IsFalseValue(getEnvironmentVariable("GIT_OPTIONAL_LOCKS")));
        }

        public static bool TryCheckOwnership(
            int pid,
            bool isElevated,
            string fullCommand,
            Func<NamedPipeMessages.Message, NamedPipeMessages.Message> sendRequest,
            out bool ownsLock,
            out string error)
        {
            ownsLock = false;
            error = null;
            NamedPipeMessages.LockRequest request = new NamedPipeMessages.LockRequest(
                pid, isElevated, checkAvailabilityOnly: true, fullCommand, gitCommandSessionId: string.Empty);
            NamedPipeMessages.Message message = sendRequest(request.CreateMessage(NamedPipeMessages.AcquireLock.AcquireRequest));
            if (message == null)
            {
                error = "GVFS returned no lock-ownership response. " + CheckMount;
                return false;
            }

            NamedPipeMessages.AcquireLock.Response response;
            try
            {
                response = new NamedPipeMessages.AcquireLock.Response(message);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
            {
                error = "GVFS returned an invalid lock-ownership response: " + ex.Message + " " + CheckMount;
                return false;
            }

            switch (response.Result)
            {
                case NamedPipeMessages.AcquireLock.AvailableResult:
                case NamedPipeMessages.AcquireLock.DenyGVFSResult:
                    return true;

                case NamedPipeMessages.AcquireLock.DenyGitResult:
                    if (response.ResponseData == null || response.ResponseData.PID <= 0)
                    {
                        error = "GVFS returned a lock owner without a valid process ID. " + CheckMount;
                        return false;
                    }

                    ownsLock = response.ResponseData.PID == pid;
                    return true;

                case NamedPipeMessages.AcquireLock.MountNotReadyResult:
                    error = "GVFS has not finished initializing. " + CheckMount;
                    return false;

                case NamedPipeMessages.AcquireLock.UnmountInProgressResult:
                    error = "GVFS is unmounting. " + CheckMount;
                    return false;

                default:
                    error = "GVFS returned an unexpected lock-ownership response: " + response.Result + ". " + CheckMount;
                    return false;
            }
        }

        private static bool IsFalseValue(string value)
        {
            return string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "no", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "off", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "0", StringComparison.OrdinalIgnoreCase);
        }
    }
}
