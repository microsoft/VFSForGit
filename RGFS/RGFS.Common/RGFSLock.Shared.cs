using RGFS.Common.NamedPipes;
using System;
using System.Diagnostics;
using System.Threading;

namespace RGFS.Common
{
    // This file contains methods that are used by RGFS.Hooks (compiled both by RGFS.Common and RGFS.Hooks).
    public partial class RGFSLock
    {
        public static bool TryAcquireRGFSLockForProcess(
            bool unattended,
            NamedPipeClient pipeClient, 
            string fullCommand, 
            int pid, 
            bool isElevated,
            Process parentProcess, 
            string rgfsEnlistmentRoot,
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
                result = "RGFS has not finished initializing, please wait a few seconds and try again.";
                return false;
            }
            else
            {
                string message = string.Empty;
                switch (response.Result)
                {
                    case NamedPipeMessages.AcquireLock.AcceptResult:
                        break;

                    case NamedPipeMessages.AcquireLock.DenyRGFSResult:
                        message = "Waiting for RGFS to release the lock";
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
                        rgfsLogEnlistmentRoot: rgfsEnlistmentRoot);
                }

                result = null;
                return true;
            }
        }

        public static void ReleaseRGFSLock(
            bool unattended,
            NamedPipeClient pipeClient,
            string fullCommand,
            int pid,
            bool isElevated,
            Process parentProcess,
            Action<NamedPipeMessages.ReleaseLock.Response> responseHandler,
            string rgfsEnlistmentRoot,
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
                    rgfsLogEnlistmentRoot: rgfsEnlistmentRoot,
                    initialDelayMs: spinnerDelay);
            }
        }
    }
}
