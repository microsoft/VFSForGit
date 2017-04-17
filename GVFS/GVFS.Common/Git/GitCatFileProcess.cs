using GVFS.Common.Tracing;
using System;
using System.Diagnostics;
using System.IO;

namespace GVFS.Common.Git
{
    public abstract class GitCatFileProcess : IDisposable
    {
        public const string TreeMarker = " tree ";
        public const string BlobMarker = " blob ";
        public const string CommitMarker = " commit ";

        protected const int ProcessReadTimeoutMs = 30000;
        protected const int ProcessShutdownTimeoutMs = 2000;

        protected readonly StreamReader StdOutCanTimeout;
        protected readonly StreamWriter StdIn;

        private Process catFileProcess;
        private WindowsProcessJob job;

        public GitCatFileProcess(ITracer tracer, Enlistment enlistment, string catFileArgs)
        {
            // This git.exe should not need/use the working directory of the repo.
            // Run git.exe in Environment.SystemDirectory to ensure the git.exe process does not touch the working directory
            this.catFileProcess = new GitProcess(enlistment).GetGitProcess(
                "cat-file " + catFileArgs,
                workingDirectory: Environment.SystemDirectory,
                dotGitDirectory: enlistment.DotGitRoot,
                useReadObjectHook: false,
                redirectStandardError: true);

            this.catFileProcess.ErrorDataReceived += (sender, args) =>
            {
                if (args.Data != null)
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata["Area"] = "GitCatFileProcess";
                    metadata["Args"] = catFileArgs;
                    metadata["Error"] = args.Data; 
                    tracer.RelatedError(metadata);
                }
            };

            this.catFileProcess.Start();

            // We have to use a job to ensure that we can kill the process correctly.  The git.exe process that we launch
            // immediately creates a child git.exe process, and if we just kill the process we created, the other one gets orphaned.
            // By adding our process to a job and closing the job, we guarantee that both processes will exit.
            this.job = new WindowsProcessJob(this.catFileProcess);

            this.StdIn = this.catFileProcess.StandardInput;
            this.StdOutCanTimeout = this.catFileProcess.StandardOutput;
        }

        public GitCatFileProcess(StreamReader stdOut, StreamWriter stdIn)
        {
            this.StdIn = stdIn;
            this.StdOutCanTimeout = stdOut;
        }

        public bool IsRunning()
        {
            return !this.catFileProcess.HasExited;
        }

        public void Dispose()
        {
            this.Kill();
        }

        public void Kill()
        {
            if (this.job != null)
            {
                this.job.Dispose();
                this.job = null;
            }

            if (this.catFileProcess != null)
            {
                this.catFileProcess.Dispose();
                this.catFileProcess = null;
            }
        }
    }
}
