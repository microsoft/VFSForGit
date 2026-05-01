using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace GVFS.FunctionalTests.Tools
{
    public static class ProcessHelper
    {
        /// <summary>
        /// Default timeout in milliseconds for child processes. -1 means infinite.
        /// Set via GVFS_FT_PROCESS_TIMEOUT_SECONDS environment variable (applies to all
        /// ProcessHelper.Run calls) or override per-call via the timeoutMs parameter.
        /// </summary>
        public static int DefaultTimeoutMs { get; set; } = ReadTimeoutFromEnvironment();

        public static ProcessResult Run(string fileName, string arguments)
        {
            return Run(fileName, arguments, workingDirectory: null);
        }

        public static ProcessResult Run(string fileName, string arguments, string workingDirectory, int timeoutMs = -1)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.CreateNoWindow = true;
            startInfo.FileName = fileName;
            startInfo.Arguments = arguments;
            if (!string.IsNullOrEmpty(workingDirectory))
            {
                startInfo.WorkingDirectory = workingDirectory;
            }

            return Run(startInfo, timeoutMs: timeoutMs);
        }

        public static ProcessResult Run(
            ProcessStartInfo processInfo,
            string errorMsgDelimeter = "\r\n",
            object executionLock = null,
            Stream inputStream = null,
            int timeoutMs = -1)
        {
            int effectiveTimeout = timeoutMs > 0 ? timeoutMs : DefaultTimeoutMs;

            using (Process executingProcess = new Process())
            {
                string output = string.Empty;
                string errors = string.Empty;

                // From https://msdn.microsoft.com/en-us/library/system.diagnostics.process.standardoutput.aspx
                // To avoid deadlocks, use asynchronous read operations on at least one of the streams.
                // Do not perform a synchronous read to the end of both redirected streams.
                executingProcess.StartInfo = processInfo;
                executingProcess.ErrorDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                    {
                        errors = errors + args.Data + errorMsgDelimeter;
                    }
                };

                if (executionLock != null)
                {
                    lock (executionLock)
                    {
                        output = StartProcess(executingProcess, inputStream, effectiveTimeout);
                    }
                }
                else
                {
                    output = StartProcess(executingProcess, inputStream, effectiveTimeout);
                }

                return new ProcessResult(output.ToString(), errors.ToString(), executingProcess.ExitCode);
            }
        }

        private static string StartProcess(Process executingProcess, Stream inputStream, int timeoutMs)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            executingProcess.Start();

            if (inputStream != null)
            {
                inputStream.CopyTo(executingProcess.StandardInput.BaseStream);
                executingProcess.StandardInput.Close();
            }

            if (executingProcess.StartInfo.RedirectStandardError)
            {
                executingProcess.BeginErrorReadLine();
            }

            string output = string.Empty;
            if (executingProcess.StartInfo.RedirectStandardOutput)
            {
                if (timeoutMs > 0)
                {
                    // Read stdout asynchronously so we can enforce a timeout on the
                    // entire process lifecycle. Without this, ReadToEnd() blocks
                    // indefinitely if the child process hangs.
                    Task<string> readTask = executingProcess.StandardOutput.ReadToEndAsync();
                    if (!readTask.Wait(timeoutMs))
                    {
                        KillProcessTree(executingProcess);
                        string processDesc = FormatProcessDescription(executingProcess);
                        throw new TimeoutException(
                            $"Process timed out after {timeoutMs / 1000}s: {processDesc}");
                    }

                    output = readTask.Result;
                }
                else
                {
                    output = executingProcess.StandardOutput.ReadToEnd();
                }
            }

            executingProcess.WaitForExit();

            if (timeoutMs > 0)
            {
                stopwatch.Stop();
                long elapsedMs = stopwatch.ElapsedMilliseconds;
                if (elapsedMs > 30_000)
                {
                    // Log slow processes to help diagnose intermittent hangs
                    string processDesc = FormatProcessDescription(executingProcess);
                    Console.WriteLine(
                        $"[{DateTime.Now:HH:mm:ss.fff}] [SLOW-PROCESS] {processDesc} " +
                        $"completed in {elapsedMs / 1000.0:F1}s (timeout: {timeoutMs / 1000}s)");
                    Console.Out.Flush();
                }
            }

            return output;
        }

        private static void KillProcessTree(Process process)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [WARN] Failed to kill process tree: {ex.Message}");
                Console.Out.Flush();
            }
        }

        private static string FormatProcessDescription(Process process)
        {
            string fileName = process.StartInfo.FileName;
            string args = process.StartInfo.Arguments;
            string workDir = process.StartInfo.WorkingDirectory;
            return $"'{fileName} {args}' (cwd: {workDir})";
        }

        private static int ReadTimeoutFromEnvironment()
        {
            string envValue = Environment.GetEnvironmentVariable("GVFS_FT_PROCESS_TIMEOUT_SECONDS");
            if (!string.IsNullOrEmpty(envValue) && int.TryParse(envValue, out int seconds) && seconds > 0)
            {
                return seconds * 1000;
            }

            return -1;
        }
    }
}
