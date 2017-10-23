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
            Process parentProcess, 
            string gvfsEnlistmentRoot,
            out string result)
        {
            NamedPipeMessages.LockRequest request = new NamedPipeMessages.LockRequest(pid, isElevated, fullCommand);

            NamedPipeMessages.Message requestMessage = request.CreateMessage(NamedPipeMessages.AcquireLock.AcquireRequest);
            pipeClient.SendRequest(requestMessage);

            NamedPipeMessages.AcquireLock.Response response = new NamedPipeMessages.AcquireLock.Response(pipeClient.ReadResponse());

            if (response.Result == NamedPipeMessages.AcquireLock.AcceptResult)
            {
                result = null;
                return true;
            }
            else if (response.Result == NamedPipeMessages.AcquireLock.MountNotReadyResult)
            {
                result = "GVFS has not finished initializing, please wait a few seconds and try again.";
                return false;
            }
            else
            {
                string message = string.Empty;
                switch (response.Result)
                {
                    case NamedPipeMessages.AcquireLock.AcceptResult:
                        break;

                    case NamedPipeMessages.AcquireLock.DenyGVFSResult:
                        message = "Waiting for GVFS to release the lock";
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
                        while (response.Result != NamedPipeMessages.AcquireLock.AcceptResult)
                        {
                            Thread.Sleep(250);
                            pipeClient.SendRequest(requestMessage);
                            response = new NamedPipeMessages.AcquireLock.Response(pipeClient.ReadResponse());
                        }

                        return true;
                    };

                if (unattended)
                {
                    waitForLock();
                }
                else
                {
                    ConsoleHelper.ShowStatusWhileRunning(
                        waitForLock,
                        message,
                        output: Console.Out,
                        showSpinner: !ConsoleHelper.IsConsoleOutputRedirectedToFile(),
                        gvfsLogEnlistmentRoot: gvfsEnlistmentRoot);
                }

                result = null;
                return true;
            }
        }

        public static void ReleaseGVFSLock(
            bool unattended,
            NamedPipeClient pipeClient,
            string fullCommand,
            int pid,
            bool isElevated,
            Process parentProcess,
            Action<NamedPipeMessages.ReleaseLock.Response> responseHandler,
            string gvfsEnlistmentRoot,
            string waitingMessage = "",
            int spinnerDelay = 0)
        {
            NamedPipeMessages.LockRequest request = new NamedPipeMessages.LockRequest(pid, isElevated, fullCommand);

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

            if (unattended || ConsoleHelper.IsConsoleOutputRedirectedToFile())
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
    }
}
