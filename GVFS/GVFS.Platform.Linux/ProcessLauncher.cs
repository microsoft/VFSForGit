using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GVFS.Platform.Linux
{
    public static class ProcessLauncher
    {
        private const int StdInFileNo  = 0; // STDIN_FILENO  -> standard input file descriptor
        private const int StdOutFileNo = 1; // STDOUT_FILENO -> standard output file descriptor
        private const int StdErrFileNo = 2; // STDERR_FILENO -> standard error file descriptor

        public static void StartBackgroundProcess(ITracer tracer, string programName, string[] args)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo(programName);
            string[] envp = NetCoreMethods.CreateEnvp(startInfo);
            string[] argv = GenerateArgv(startInfo.FileName, args);

            unsafe
            {
                byte** argvPtr = null;
                byte** envpPtr = null;

                try
                {
                    NetCoreMethods.AllocNullTerminatedArray(argv, ref argvPtr);
                    NetCoreMethods.AllocNullTerminatedArray(envp, ref envpPtr);

                    // Fork the child process
                    int processId = Fork();
                    if (processId == -1)
                    {
                        string errorMessage = "Failed to fork process";
                        EventMetadata metadata = new EventMetadata();
                        metadata.Add("lastErrorCode", Marshal.GetLastWin32Error());
                        tracer.RelatedError(metadata, errorMessage);
                        throw new Win32Exception(Marshal.GetLastWin32Error(), errorMessage);
                    }

                    if (processId == 0)
                    {
                        RunChildProcess(tracer, programName, argvPtr, envpPtr);
                    }
                }
                finally
                {
                    NetCoreMethods.FreeArray(envpPtr, envp.Length);
                    NetCoreMethods.FreeArray(argvPtr, argv.Length);
                }
            }
        }

        private static unsafe void RunChildProcess(
            ITracer tracer,
            string programName,
            byte** argvPtr = null,
            byte** envpPtr = null)
        {
            int fdin = Open("/dev/null", (int)NetCoreMethods.OpenFlags.O_RDONLY);
            if (fdin == -1)
            {
                LogErrorAndExit(tracer, "Unable to open file descriptor for stdin", Marshal.GetLastWin32Error());
            }

            int fdout = Open("/dev/null", (int)NetCoreMethods.OpenFlags.O_WRONLY);
            if (fdout == -1)
            {
                LogErrorAndExit(tracer, "Unable to open file descriptor for stdout", Marshal.GetLastWin32Error());
            }

            // Redirect stdout/stdin/stderr to "/dev/null"
            if (Dup2(fdin, StdInFileNo) == -1 ||
                Dup2(fdout, StdOutFileNo) == -1 ||
                Dup2(fdout, StdErrFileNo) == -1)
            {
                LogErrorAndExit(tracer, "Error redirecting stdout/stdin/stderr", Marshal.GetLastWin32Error());
            }

            // Become session leader of a new session
            if (SetSid() == -1)
            {
                LogErrorAndExit(tracer, "Error calling SetSid()", Marshal.GetLastWin32Error());
            }

            // execve will not return if it's successful.
            Execve(programName, argvPtr, envpPtr);
            LogErrorAndExit(tracer, "Error calling Execve", Marshal.GetLastWin32Error());
        }

        private static string[] GenerateArgv(string fileName, string[] args)
        {
            List<string> argvList = new List<string>();
            argvList.Add(fileName);
            argvList.AddRange(args);
            return argvList.ToArray();
        }

        private static void LogErrorAndExit(ITracer tracer, string message, int lastErrorCode)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add(nameof(lastErrorCode), lastErrorCode);
            tracer.RelatedError(metadata, message);
            Environment.Exit(lastErrorCode);
        }

        [DllImport("libc", EntryPoint = "fork", SetLastError = true)]
        private static extern int Fork();

        [DllImport("libc", EntryPoint = "setsid", SetLastError = true)]
        private static extern int SetSid();

        [DllImport("libc", EntryPoint = "open", SetLastError = true)]
        private static extern int Open(string path, int flag);

        [DllImport("libc", EntryPoint = "dup2", SetLastError = true)]
        private static extern int Dup2(int oldfd, int newfd);

        [DllImport("libc", EntryPoint = "execve", SetLastError = true)]
        private static extern unsafe int Execve(string filename, byte** argv, byte** envp);
    }
}
