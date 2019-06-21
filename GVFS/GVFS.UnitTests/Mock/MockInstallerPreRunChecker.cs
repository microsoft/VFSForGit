using GVFS.Common.Tracing;
using GVFS.Upgrader;
using System;
using System.Collections.Generic;

namespace GVFS.UnitTests.Mock.Upgrader
{
    public class MockInstallerPrerunChecker : InstallerPreRunChecker
    {
        public const string GitUpgradeCheckError = "Unable to upgrade Git";

        private FailOnCheckType failOnCheck;

        public MockInstallerPrerunChecker(ITracer tracer) : base(tracer, string.Empty)
        {
        }

        [Flags]
        public enum FailOnCheckType
        {
            Invalid = 0,
            ProjFSEnabled = 0x1,
            IsElevated = 0x2,
            BlockingProcessesRunning = 0x4,
            UnattendedMode = 0x8,
            UnMountRepos = 0x10,
            RemountRepos = 0x20,
            IsServiceInstalledAndNotRunning = 0x40,
        }

        public void SetReturnFalseOnCheck(FailOnCheckType prerunCheck)
        {
            this.failOnCheck |= prerunCheck;
        }

        public void SetReturnTrueOnCheck(FailOnCheckType prerunCheck)
        {
            this.failOnCheck &= ~prerunCheck;
        }

        public void Reset()
        {
            this.failOnCheck = FailOnCheckType.Invalid;

            this.SetReturnFalseOnCheck(MockInstallerPrerunChecker.FailOnCheckType.UnattendedMode);
            this.SetReturnFalseOnCheck(MockInstallerPrerunChecker.FailOnCheckType.BlockingProcessesRunning);
            this.SetReturnFalseOnCheck(MockInstallerPrerunChecker.FailOnCheckType.IsServiceInstalledAndNotRunning);
        }

        public void SetCommandToRerun(string command)
        {
            this.CommandToRerun = command;
        }

        protected override bool IsServiceInstalledAndNotRunning()
        {
            return this.FakedResultOfCheck(FailOnCheckType.IsServiceInstalledAndNotRunning);
        }

        protected override bool IsElevated()
        {
            return this.FakedResultOfCheck(FailOnCheckType.IsElevated);
        }

        protected override bool IsGVFSUpgradeSupported()
        {
            return this.FakedResultOfCheck(FailOnCheckType.ProjFSEnabled);
        }

        protected override bool IsUnattended()
        {
            return this.FakedResultOfCheck(FailOnCheckType.UnattendedMode);
        }

        protected override bool IsBlockingProcessRunning(out HashSet<string> processes)
        {
            processes = new HashSet<string>();

            bool isRunning = this.FakedResultOfCheck(FailOnCheckType.BlockingProcessesRunning);
            if (isRunning)
            {
                processes.Add("GVFS.Mount");
                processes.Add("git");
            }

            return isRunning;
        }

        protected override bool TryRunGVFSWithArgs(string args, out string error)
        {
            if (string.CompareOrdinal(args, "service --unmount-all") == 0)
            {
                bool result = this.FakedResultOfCheck(FailOnCheckType.UnMountRepos);
                error = result == false ? "Unmount of some of the repositories failed." : null;
                return result;
            }

            if (string.CompareOrdinal(args, "service --mount-all") == 0)
            {
                bool result = this.FakedResultOfCheck(FailOnCheckType.RemountRepos);
                error = result == false ? "Auto remount failed." : null;
                return result;
            }

            error = "Unknown GVFS command";
            return false;
        }

        private bool FakedResultOfCheck(FailOnCheckType checkType)
        {
            return !this.failOnCheck.HasFlag(checkType);
        }
    }
}
