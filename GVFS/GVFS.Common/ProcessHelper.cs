using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace GVFS.Common
{
    public static class ProcessHelper
    {
        private static string currentProcessVersion = null;

        public static ProcessResult Run(string programName, string args, bool redirectOutput = true)
        {
            ProcessStartInfo processInfo = new ProcessStartInfo(programName);
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardInput = true;
            processInfo.RedirectStandardOutput = redirectOutput;
            processInfo.RedirectStandardError = redirectOutput;
            processInfo.WindowStyle = ProcessWindowStyle.Hidden;
            processInfo.CreateNoWindow = redirectOutput;
            processInfo.Arguments = args;

            return Run(processInfo);
        }

        public static string GetCurrentProcessLocation()
        {
            // Environment.ProcessPath can be null in NativeAOT or certain hosting scenarios.
            string processPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(processPath))
            {
                return Path.GetDirectoryName(processPath);
            }

            return AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        }

        public static string GetEntryClassName()
        {
            // AppDomain.FriendlyName is reliable even when Assembly.GetEntryAssembly() returns null.
            string friendlyName = AppDomain.CurrentDomain.FriendlyName;
            if (!string.IsNullOrEmpty(friendlyName))
            {
                return Path.GetFileNameWithoutExtension(friendlyName);
            }

            Assembly assembly = Assembly.GetEntryAssembly();
            if (assembly == null)
            {
                assembly = Assembly.GetExecutingAssembly();
            }

            return assembly.GetName().Name;
        }

        public static string GetCurrentProcessVersion()
        {
            if (currentProcessVersion == null)
            {
                string processPath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(processPath))
                {
                    FileVersionInfo fileVersionInfo = FileVersionInfo.GetVersionInfo(processPath);
                    currentProcessVersion = fileVersionInfo.ProductVersion;
                }
                else
                {
                    currentProcessVersion = "0.0.0.0";
                }
            }

            return currentProcessVersion;
        }

        public static bool IsDevelopmentVersion()
        {
            // Official CI builds use version numbers where major > 0.
            // Development builds always start with 0.
            string version = ProcessHelper.GetCurrentProcessVersion();
            return version.StartsWith("0.");
        }

        public static string GetProgramLocation(string programLocaterCommand, string processName)
        {
            ProcessResult result = ProcessHelper.Run(programLocaterCommand, processName);
            if (result.ExitCode != 0)
            {
                return null;
            }

            string firstPath =
                string.IsNullOrWhiteSpace(result.Output)
                ? null
                : result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (firstPath == null)
            {
                return null;
            }

            try
            {
                return Path.GetDirectoryName(firstPath);
            }
            catch (IOException)
            {
                return null;
            }
        }

        public static ProcessResult Run(ProcessStartInfo processInfo, string errorMsgDelimeter = "\r\n", object executionLock = null)
        {
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
                        output = StartProcess(executingProcess);
                    }
                }
                else
                {
                    output = StartProcess(executingProcess);
                }

                return new ProcessResult(output.ToString(), errors.ToString(), executingProcess.ExitCode);
            }
        }

        private static string StartProcess(Process executingProcess)
        {
            executingProcess.Start();

            if (executingProcess.StartInfo.RedirectStandardError)
            {
                executingProcess.BeginErrorReadLine();
            }

            string output = string.Empty;
            if (executingProcess.StartInfo.RedirectStandardOutput)
            {
                output = executingProcess.StandardOutput.ReadToEnd();
            }

            executingProcess.WaitForExit();

            return output;
        }
    }
}
