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

                    tracer.RelatedInfo($"{nameof(StartBackgroundProcess)}: Forking process and starting {programName}");

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
            // The daemon() function is for programs wishing to detach themselves
            // from the controlling terminal and run in the background as system
            // daemons.
            //
            // If nochdir is zero, daemon() changes the process's current working
            // directory to the root directory("/"); otherwise, the current working
            // directory is left unchanged.
            //
            // If noclose is zero, daemon() redirects standard input, standard
            // output and standard error to /dev/ null; otherwise, no changes are
            // made to these file descriptors.
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

        [DllImport("libc", EntryPoint = "daemon", SetLastError = true)]
        private static extern int Daemon(int nochdir, int noclose);

        [DllImport("libc", EntryPoint = "execve", SetLastError = true)]
        private static extern unsafe int Execve(string filename, byte** argv, byte** envp);
    }
}
