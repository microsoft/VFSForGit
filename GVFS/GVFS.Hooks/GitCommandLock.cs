using GVFS.Common.NamedPipes;
using System;

namespace GVFS.Hooks
{
    internal static class GitCommandLock
    {
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
                error = "GVFS returned no lock-ownership response. Run 'gvfs status' to check the mount.";
                return false;
            }

            NamedPipeMessages.AcquireLock.Response response;
            try
            {
                response = new NamedPipeMessages.AcquireLock.Response(message);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
            {
                error = "GVFS returned an invalid lock-ownership response: " + ex.Message;
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
                        error = "GVFS returned a lock owner without a valid process ID.";
                        return false;
                    }

                    ownsLock = response.ResponseData.PID == pid;
                    return true;

                case NamedPipeMessages.AcquireLock.MountNotReadyResult:
                    error = "GVFS has not finished initializing. Run 'gvfs status' to check the mount.";
                    return false;

                case NamedPipeMessages.AcquireLock.UnmountInProgressResult:
                    error = "GVFS is unmounting.";
                    return false;

                default:
                    error = "GVFS returned an unexpected lock-ownership response: " + response.Result;
                    return false;
            }
        }
    }
}
