using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GVFS.Platform.POSIX
{
    public static class ProcessLauncher
    {
        private const int StdInFileNo = 0; // STDIN_FILENO  -> standard input file descriptor
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

                    tracer.RelatedInfo($"{nameof(StartBackgroundProcess)}: About to fork process");

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
                        RunChildProcess(programName, argvPtr, envpPtr);
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
            string programName,
            byte** argvPtr = null,
            byte** envpPtr = null)
        {
            Close(StdInFileNo);
            Close(StdOutFileNo);
            Close(StdErrFileNo);

            // Detach and run as system daemon
            // Unless the argument nochdir is non - zero, daemon() changes the current working directory to the root(/).
            // Unless the argument noclose is non - zero, daemon() will redirect standard input, standard output, and standard error to /dev/null.
            if (Daemon(nochdir: 1, noclose: 0) != 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to daemonize process");
            }

            // execve will not return if it's successful.
            Execve(programName, argvPtr, envpPtr);
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Error calling Execve");
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

        [DllImport("libc", EntryPoint = "close", SetLastError = true)]
        private static extern int Close(int filedes);

        [DllImport("libc", EntryPoint = "dup2", SetLastError = true)]
        private static extern int Dup2(int oldfd, int newfd);

        [DllImport("libc", EntryPoint = "daemon", SetLastError = true)]
        private static extern int Daemon(int nochdir, int noclose);

        [DllImport("libc", EntryPoint = "execve", SetLastError = true)]
        private static extern unsafe int Execve(string filename, byte** argv, byte** envp);
    }
}
