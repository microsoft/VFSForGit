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
            Assembly assembly = Assembly.GetExecutingAssembly();
            return Path.GetDirectoryName(assembly.Location);
        }

        public static string GetEntryClassName()
        {
            Assembly assembly = Assembly.GetEntryAssembly();
            if (assembly == null)
            {
                // The PR build tests doesn't produce an entry assembly because it is run from unmanaged code,
                // so we'll fall back on using this assembly. This should never ever happen for a normal exe invocation.
                assembly = Assembly.GetExecutingAssembly();
            }

            return assembly.GetName().Name;
        }

        public static string GetCurrentProcessVersion()
        {
            if (currentProcessVersion == null)
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                FileVersionInfo fileVersionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
                currentProcessVersion = fileVersionInfo.ProductVersion;
            }

            return currentProcessVersion;
        }

        public static bool IsDevelopmentVersion()
        {
            string version = ProcessHelper.GetCurrentProcessVersion();
            return version.Equals("0.2.173.2") || version.StartsWith("0.2.173.2+");
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
