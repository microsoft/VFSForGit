using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace GVFS.Common
{
    public static class ConsoleHelper
    {
        public enum ActionResult
        {
            Success,
            CompletedWithErrors,
            Failure,
        }

        public static bool ShowStatusWhileRunning(
            Func<bool> action,
            string message,
            TextWriter output,
            bool showSpinner,
            string gvfsLogEnlistmentRoot,
            int initialDelayMs = 0)
        {
            Func<ActionResult> actionResultAction =
                () =>
                {
                    return action() ? ActionResult.Success : ActionResult.Failure;
                };

            ActionResult result = ShowStatusWhileRunning(
                actionResultAction,
                message,
                output,
                showSpinner,
                gvfsLogEnlistmentRoot,
                initialDelayMs: initialDelayMs);

            return result == ActionResult.Success;
        }

        public static ActionResult ShowStatusWhileRunning(
            Func<ActionResult> action,
            string message,
            TextWriter output,
            bool showSpinner,
            string gvfsLogEnlistmentRoot,
            int initialDelayMs)
        {
            ActionResult result = ActionResult.Failure;
            bool initialMessageWritten = false;

            try
            {
                if (!showSpinner)
                {
                    output.Write(message + "...");
                    initialMessageWritten = true;
                    result = action();
                }
                else
                {
                    ManualResetEvent actionIsDone = new ManualResetEvent(false);
                    bool isComplete = false;
                    Thread spinnerThread = new Thread(
                        () =>
                        {
                            int retries = 0;
                            char[] waiting = { '\u2014', '\\', '|', '/' };

                            while (!isComplete)
                            {
                                if (retries == 0)
                                {
                                    actionIsDone.WaitOne(initialDelayMs);
                                }
                                else
                                {
                                    output.Write("\r{0}...{1}", message, waiting[(retries / 2) % waiting.Length]);
                                    initialMessageWritten = true;
                                    actionIsDone.WaitOne(100);
                                }

                                retries++;
                            }

                            if (initialMessageWritten)
                            {
                                // Clear out any trailing waiting character
                                output.Write("\r{0}...", message);
                            }
                        });
                    spinnerThread.Start();

                    try
                    {
                        result = action();
                    }
                    finally
                    {
                        isComplete = true;

                        actionIsDone.Set();
                        spinnerThread.Join();
                    }
                }
            }
            finally
            {
                switch (result)
                {
                    case ActionResult.Success:
                        if (initialMessageWritten)
                        {
                            output.WriteLine("Succeeded");
                        }

                        break;

                    case ActionResult.CompletedWithErrors:
                        if (!initialMessageWritten)
                        {
                            output.Write("\r{0}...", message);
                        }

                        output.WriteLine("Completed with errors.");
                        break;

                    case ActionResult.Failure:
                        if (!initialMessageWritten)
                        {
                            output.Write("\r{0}...", message);
                        }

                        output.WriteLine("Failed" + (gvfsLogEnlistmentRoot == null ? string.Empty : ". " + GetGVFSLogMessage(gvfsLogEnlistmentRoot)));
                        break;
                }
            }

            return result;
        }

        public static string GetGVFSLogMessage(string enlistmentRoot)
        {
            return "Run 'gvfs log " + enlistmentRoot + "' for more info.";
        }
    }
}
