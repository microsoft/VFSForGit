using GVFS.Common.NamedPipes;
using System;
using System.Diagnostics;
using System.Threading;

namespace GVFS.Common
{
    // This file contains methods that are used by GVFS.Hooks (compiled both by GVFS.Common and GVFS.Hooks).
    public partial class GVFSLock
    {
        public static bool TryAcquireGVFSLockForProcess(
            bool unattended,
            NamedPipeClient pipeClient,
            string fullCommand,
            int pid,
            bool isElevated,
            bool isConsoleOutputRedirectedToFile,
            bool checkAvailabilityOnly,
            string gvfsEnlistmentRoot,
            string gitCommandSessionId,
            out string result)
        {
            NamedPipeMessages.LockRequest request = new NamedPipeMessages.LockRequest(pid, isElevated, checkAvailabilityOnly, fullCommand, gitCommandSessionId);

            NamedPipeMessages.Message requestMessage = request.CreateMessage(NamedPipeMessages.AcquireLock.AcquireRequest);
            pipeClient.SendRequest(requestMessage);

            NamedPipeMessages.AcquireLock.Response response = new NamedPipeMessages.AcquireLock.Response(pipeClient.ReadResponse());

            string message = string.Empty;
            switch (response.Result)
            {
                case NamedPipeMessages.AcquireLock.AcceptResult:
                case NamedPipeMessages.AcquireLock.AvailableResult:
                    return CheckAcceptResponse(response, checkAvailabilityOnly, out result);

                case NamedPipeMessages.AcquireLock.MountNotReadyResult:
                    result = "GVFS has not finished initializing, please wait a few seconds and try again.";
                    return false;

                case NamedPipeMessages.AcquireLock.UnmountInProgressResult:
                    result = "GVFS is unmounting.";
                    return false;

                case NamedPipeMessages.AcquireLock.DenyGVFSResult:
                    message = response.DenyGVFSMessage;
                    break;

                case NamedPipeMessages.AcquireLock.DenyGitResult:
                    message = string.Format("Waiting for '{0}' to release the lock", response.ResponseData.ParsedCommand);
                    break;

                default:
                    result = "Error when acquiring the lock. Unrecognized response: " + response.CreateMessage();
                    return false;
            }

            Func<bool> waitForLock =
                () =>
                {
                    while (true)
                    {
                        Thread.Sleep(250);
                        pipeClient.SendRequest(requestMessage);
                        response = new NamedPipeMessages.AcquireLock.Response(pipeClient.ReadResponse());
                        switch (response.Result)
                        {
                            case NamedPipeMessages.AcquireLock.AcceptResult:
                            case NamedPipeMessages.AcquireLock.AvailableResult:
                                return CheckAcceptResponse(response, checkAvailabilityOnly, out _);

                            case NamedPipeMessages.AcquireLock.UnmountInProgressResult:
                                return false;

                            default:
                                break;
                        }
                    }
                };

            bool isSuccessfulLockResult;
            if (unattended)
            {
                isSuccessfulLockResult = waitForLock();
            }
            else
            {
                isSuccessfulLockResult = ConsoleHelper.ShowStatusWhileRunning(
                    waitForLock,
                    message,
                    output: Console.Out,
                    showSpinner: !isConsoleOutputRedirectedToFile,
                    gvfsLogEnlistmentRoot: gvfsEnlistmentRoot);
            }

            result = null;
            return isSuccessfulLockResult;
        }

        public static void ReleaseGVFSLock(
            bool unattended,
            NamedPipeClient pipeClient,
            string fullCommand,
            int pid,
            bool isElevated,
            bool isConsoleOutputRedirectedToFile,
            Action<NamedPipeMessages.ReleaseLock.Response> responseHandler,
            string gvfsEnlistmentRoot,
            string waitingMessage = "",
            int spinnerDelay = 0)
        {
            NamedPipeMessages.LockRequest request = new NamedPipeMessages.LockRequest(pid, isElevated, checkAvailabilityOnly: false, parsedCommand: fullCommand, gitCommandSessionId: string.Empty);

            NamedPipeMessages.Message requestMessage = request.CreateMessage(NamedPipeMessages.ReleaseLock.Request);

            pipeClient.SendRequest(requestMessage);
            NamedPipeMessages.ReleaseLock.Response response = null;

            Func<ConsoleHelper.ActionResult> releaseLock =
                () =>
                {
                    response = new NamedPipeMessages.ReleaseLock.Response(pipeClient.ReadResponse());
                    responseHandler(response);
                    return ConsoleHelper.ActionResult.Success;
                };

            if (unattended || isConsoleOutputRedirectedToFile)
            {
                releaseLock();
            }
            else
            {
                ConsoleHelper.ShowStatusWhileRunning(
                    releaseLock,
                    waitingMessage,
                    output: Console.Out,
                    showSpinner: true,
                    gvfsLogEnlistmentRoot: gvfsEnlistmentRoot,
                    initialDelayMs: spinnerDelay);
            }
        }

        private static bool CheckAcceptResponse(NamedPipeMessages.AcquireLock.Response response, bool checkAvailabilityOnly, out string message)
        {
            switch (response.Result)
            {
                case NamedPipeMessages.AcquireLock.AcceptResult:
                    if (!checkAvailabilityOnly)
                    {
                        message = null;
                        return true;
                    }
                    else
                    {
                        message = "Error when acquiring the lock. Unexpected response: " + response.CreateMessage();
                        return false;
                    }

                case NamedPipeMessages.AcquireLock.AvailableResult:
                    if (checkAvailabilityOnly)
                    {
                        message = null;
                        return true;
                    }
                    else
                    {
                        message = "Error when acquiring the lock. Unexpected response: " + response.CreateMessage();
                        return false;
                    }

                default:
                    message = "Error when acquiring the lock. Not an Accept result: " + response.CreateMessage();
                    return false;
            }
        }
    }
}
