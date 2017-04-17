using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace GVFS.Common
{
    public static class ConsoleHelper
    {
        private enum StdHandle
        {
            Stdin = -10,
            Stdout = -11,
            Stderr = -12
        }

        private enum FileType : uint
        {
            Unknown = 0x0000,
            Disk = 0x0001,
            Char = 0x0002,
            Pipe = 0x0003,
            Remote = 0x8000,
        }

        public static bool ShowStatusWhileRunning(Func<bool> action, string message, TextWriter output, bool showSpinner, bool suppressGvfsLogMessage = false)
        {
            bool result;

            if (!showSpinner)
            {
                output.Write(message + "...");
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
                            output.Write("\r{0}...{1}", message, waiting[(retries / 2) % waiting.Length]);

                            actionIsDone.WaitOne(100);
                            retries++;
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

                output.Write("\r{0}...", message);
            }

            if (result)
            {
                output.WriteLine("Succeeded");
            }
            else
            {
                output.WriteLine("Failed" + (suppressGvfsLogMessage ? string.Empty : " . Run 'gvfs log' for more info."));
            }

            return result;
        }

        public static bool IsConsoleOutputRedirectedToFile()
        {
            return FileType.Disk == GetFileType(GetStdHandle(StdHandle.Stdout));
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetStdHandle(StdHandle std);

        [DllImport("kernel32.dll")]
        private static extern FileType GetFileType(IntPtr hdl);
    }
}
