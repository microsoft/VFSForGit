using CommandLine;
using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using System;
using System.Diagnostics;
using System.IO;

namespace GVFS.CommandLine
{
    [Verb(UpgradeVerbName, HelpText = "Checks for new GVFS release, downloads and installs it when available.")]
    public class UpgradeVerb : GVFSVerb.ForNoEnlistment
    {
        private const string UpgradeVerbName = "upgrade";
        private const string DryRunOption = "--dry-run";
        private const string NoVerifyOption = "--no-verify";
        private const string ConfirmOption = "--confirm";

        private ITracer tracer;
        private PhysicalFileSystem fileSystem;
        private ProcessLauncher processLauncher;

        public UpgradeVerb(
            ITracer tracer,
            PhysicalFileSystem fileSystem,
            ProcessLauncher processWrapper,
            TextWriter output)
        {
            this.tracer = tracer;
            this.fileSystem = fileSystem;
            this.processLauncher = processWrapper;
            this.Output = output;
        }

        public UpgradeVerb()
        {
            this.fileSystem = new PhysicalFileSystem();
            this.processLauncher = new ProcessLauncher();
            this.Output = Console.Out;
        }

        [Option(
            "confirm",
            Default = false,
            Required = false,
            HelpText = "Pass in this flag to actually install the newest release")]
        public bool Confirmed { get; set; }

        [Option(
            "dry-run",
            Default = false,
            Required = false,
            HelpText = "Display progress and errors, but don't install GVFS")]
        public bool DryRun { get; set; }

        [Option(
            "no-verify",
            Default = false,
            Required = false,
            HelpText = "Do not verify NuGet packages after downloading them. Some platforms do not support NuGet verification.")]
        public bool NoVerify { get; set; }

        protected override string VerbName
        {
            get { return UpgradeVerbName; }
        }

        public override void Execute()
        {
            this.ReportErrorAndExit(this.tracer, ReturnCode.GenericError, "failed to upgrade");
        }

        public class ProcessLauncher
        {
            public ProcessLauncher()
            {
                this.Process = new Process();
            }

            public Process Process { get; private set; }

            public virtual bool HasExited
            {
                get { return this.Process.HasExited; }
            }

            public virtual int ExitCode
            {
                get { return this.Process.ExitCode; }
            }

            public virtual void WaitForExit()
            {
                this.Process.WaitForExit();
            }

            public virtual bool TryStart(string path, string args, bool useShellExecute, out Exception exception)
            {
                this.Process.StartInfo = new ProcessStartInfo(path)
                {
                    UseShellExecute = useShellExecute,
                    WorkingDirectory = Environment.SystemDirectory,
                    WindowStyle = ProcessWindowStyle.Normal,
                    Arguments = args
                };

                exception = null;

                try
                {
                    return this.Process.Start();
                }
                catch (Exception ex)
                {
                    exception = ex;
                }

                return false;
            }
        }
    }
}
