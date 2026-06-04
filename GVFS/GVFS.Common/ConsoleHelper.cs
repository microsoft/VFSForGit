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
            return ShowStatusWhileRunning(
                action,
                getMessage: null,
                message: message,
                output,
                showSpinner,
                gvfsLogEnlistmentRoot,
                initialDelayMs);
        }

        /// <summary>
        /// Runs an action while displaying a dynamic status message with a spinner.
        /// The <paramref name="getMessage"/> delegate is called on each spinner tick
        /// and may return a sub-status string (e.g. "Authenticating") that is appended
        /// to <paramref name="message"/> in parentheses. When null or returning null,
        /// only the base message is shown.
        /// </summary>
        public static bool ShowStatusWhileRunning(
            Func<bool> action,
            Func<string> getMessage,
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
                getMessage,
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
            return ShowStatusWhileRunning(action, getMessage: null, message, output, showSpinner, gvfsLogEnlistmentRoot, initialDelayMs);
        }

        public static ActionResult ShowStatusWhileRunning(
            Func<ActionResult> action,
            Func<string> getMessage,
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
                            string lastProgress = null;

                            while (!isComplete)
                            {
                                if (retries == 0)
                                {
                                    actionIsDone.WaitOne(initialDelayMs);
                                }
                                else
                                {
                                    string progress = getMessage?.Invoke();
                                    string displayMessage = !string.IsNullOrEmpty(progress)
                                        ? $"{message} ({progress})"
                                        : message;

                                    // Clear previous line content when message shrinks
                                    string line = $"\r{displayMessage}...{waiting[(retries / 2) % waiting.Length]}";
                                    if (lastProgress != null && lastProgress.Length > line.Length)
                                    {
                                        output.Write(line + new string(' ', lastProgress.Length - line.Length));
                                    }
                                    else
                                    {
                                        output.Write(line);
                                    }

                                    lastProgress = line;
                                    initialMessageWritten = true;
                                    actionIsDone.WaitOne(100);
                                }

                                retries++;
                            }

                            if (initialMessageWritten)
                            {
                                // Clear out any trailing waiting character and sub-status
                                string finalLine = $"\r{message}...";
                                if (lastProgress != null && lastProgress.Length > finalLine.Length)
                                {
                                    output.Write(finalLine + new string(' ', lastProgress.Length - finalLine.Length) + $"\r{message}...");
                                }
                                else
                                {
                                    output.Write(finalLine);
                                }
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
