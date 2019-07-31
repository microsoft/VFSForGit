using System;

namespace GVFS.UnitTests.Windows.Upgrader
{
    public class MockProcessLauncher : GVFS.CommandLine.UpgradeVerb.ProcessLauncher
    {
        private int exitCode;
        private bool hasExited;
        private bool startResult;

        public MockProcessLauncher(
            int exitCode,
            bool hasExited,
            bool startResult)
        {
            this.exitCode = exitCode;
            this.hasExited = hasExited;
            this.startResult = startResult;
        }

        public bool IsLaunched { get; private set; }

        public string LaunchPath { get; private set; }

        public override bool HasExited
        {
            get { return this.hasExited; }
        }

        public override int ExitCode
        {
            get { return this.exitCode; }
        }

        public override bool TryStart(string path, string args, bool useShellExecute, out Exception exception)
        {
            this.LaunchPath = path;
            this.IsLaunched = true;

            exception = null;
            return this.startResult;
        }
    }
}
