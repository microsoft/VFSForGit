using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace GVFS.Common
{
    public static class ProcessHelper
    {
        /// <summary>
        /// Get the process Id for the highest process with the given name in the current process hierarchy.
        /// </summary>
        /// <param name="parentName">The name of the parent process to consider (e.g. git.exe)</param>
        /// <returns>The process Id or -1 if not found.</returns>
        public static int GetParentProcessId(string parentName)
        {
            Dictionary<int, Process> processesSnapshot = Process.GetProcesses().ToDictionary(p => p.Id);

            int highestParentId = GVFSConstants.InvalidProcessId;
            Process currentProcess = Process.GetCurrentProcess();
            while (true)
            {
                ProcessBasicInformation processBasicInfo;
                int size;
                int result =
                    NtQueryInformationProcess(
                        currentProcess.Handle,
                        0, // Denotes ProcessBasicInformation
                        out processBasicInfo,
                        Marshal.SizeOf(typeof(ProcessBasicInformation)),
                        out size);

                int potentialParentId = processBasicInfo.InheritedFromUniqueProcessId.ToInt32();
                if (result != 0 || potentialParentId == 0)
                {
                    return GetProcessIdIfHasName(highestParentId, parentName);
                }

                Process processFound;
                if (processesSnapshot.TryGetValue(potentialParentId, out processFound))
                {
                    if (processFound.MainModule.ModuleName.Equals(parentName, StringComparison.OrdinalIgnoreCase))
                    {
                        highestParentId = potentialParentId;
                    }
                    else if (highestParentId > 0)
                    {
                        return GetProcessIdIfHasName(highestParentId, parentName);
                    }
                }
                else 
                {
                    if (highestParentId > 0)
                    {
                        return GetProcessIdIfHasName(highestParentId, parentName);
                    }

                    return GVFSConstants.InvalidProcessId;
                }

                currentProcess = Process.GetProcessById(potentialParentId);
            }
        }

        public static bool TryGetProcess(int processId, out Process process)
        {
            try
            {
                process = Process.GetProcessById(processId);
                return true;
            }
            catch (ArgumentException)
            {
                process = null;
                return false;
            }
        }

        public static ProcessResult Run(string programName, string args, bool redirectOutput = true)
        {
            ProcessStartInfo processInfo = new ProcessStartInfo(programName);
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardInput = true;
            processInfo.RedirectStandardOutput = redirectOutput;
            processInfo.RedirectStandardError = redirectOutput;
            processInfo.WindowStyle = ProcessWindowStyle.Hidden;
            processInfo.Arguments = args;

            return Run(processInfo);
        }

        public static void StartBackgroundProcess(string programName, string args, bool createWindow)
        {
            ProcessStartInfo processInfo = new ProcessStartInfo(programName, args);

            if (createWindow)
            {
                processInfo.WindowStyle = ProcessWindowStyle.Minimized;
            }
            else
            {
                processInfo.WindowStyle = ProcessWindowStyle.Hidden;
            }

            Process executingProcess = new Process();
            executingProcess.StartInfo = processInfo;

            executingProcess.Start();
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
            Assembly assembly = Assembly.GetExecutingAssembly();
            FileVersionInfo fileVersionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
            return fileVersionInfo.ProductVersion;
        }

        public static bool IsAdminElevated()
        {
            using (WindowsIdentity id = WindowsIdentity.GetCurrent())
            {
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        public static string WhereDirectory(string processName)
        {
            ProcessResult result = ProcessHelper.Run("where", processName);
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

        public static string GetCommandLine(Process process)
        {
            using (ManagementObjectSearcher wmiSearch = 
                new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + process.Id))
            {
                foreach (ManagementBaseObject commandLineObject in wmiSearch.Get())
                {
                    return process.StartInfo.FileName + " " + commandLineObject["CommandLine"];
                }
            }

            return string.Empty;
        }

        private static int GetProcessIdIfHasName(int processId, string expectedName)
        {
            if (ProcessIdHasName(processId, expectedName))
            {
                return processId;
            }
            else
            {
                return GVFSConstants.InvalidProcessId;
            }
        }

        private static bool ProcessIdHasName(int processId, string expectedName)
        {
            Process process;
            if (TryGetProcess(processId, out process))
            {
                return process.MainModule.ModuleName.Equals(expectedName, StringComparison.OrdinalIgnoreCase);
            }

            return false;
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

        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(
            IntPtr processHandle,
            int processInformationClass,
            out ProcessBasicInformation processInformation,
            int processInformationLength,
            out int returnLength);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessBasicInformation
        {
            public IntPtr ExitStatus;
            public IntPtr PebBaseAddress;
            public IntPtr AffinityMask;
            public IntPtr BasePriority;
            public UIntPtr UniqueProcessId;
            public IntPtr InheritedFromUniqueProcessId;
        }
    }
}
