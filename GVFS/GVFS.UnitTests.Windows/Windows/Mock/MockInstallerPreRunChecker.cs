using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using GVFS.UnitTests.Windows.Upgrader;
using GVFS.Upgrader;
using System;
using System.Collections.Generic;
using System.IO;

namespace GVFS.UnitTests.Windows.Mock.Upgrader
{
    public class MockInstallerPrerunChecker : InstallerPreRunChecker
    {
        public const string GitUpgradeCheckError = "Unable to upgrade Git";

        private FailOnCheckType failOnCheck;

        public MockInstallerPrerunChecker(CallSequenceTracker callSequenceTracker, ITracer tracer) : base(tracer)
        {
            this.SequenceTracker = callSequenceTracker;
        }

        [Flags]
        public enum FailOnCheckType
        {
            Invalid = 0,
            ProjFSEnabled = 0x1,
            IsElevated = 0x2,
            BlockingProcessesRunning = 0x4,
            UnattendedMode = 0x8,
            IsDevelopmentVersion = 0x10,
            IsGitUpgradeAllowed = 0x20,
            UnMountRepos = 0x40,
            RemountRepos = 0x80,
            IsServiceInstalledAndNotRunning = 0x100
        }
        
        public List<string> GVFSArgs { get; private set; } = new List<string>();

        public CallSequenceTracker SequenceTracker { get; private set; }

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

            this.SetReturnFalseOnCheck(MockInstallerPrerunChecker.FailOnCheckType.IsDevelopmentVersion);
            this.SetReturnFalseOnCheck(MockInstallerPrerunChecker.FailOnCheckType.UnattendedMode);
            this.SetReturnFalseOnCheck(MockInstallerPrerunChecker.FailOnCheckType.BlockingProcessesRunning);
            this.SetReturnFalseOnCheck(MockInstallerPrerunChecker.FailOnCheckType.IsServiceInstalledAndNotRunning);

            this.GVFSArgs.Clear();
        }

        public override bool TryGetGitVersion(out GitVersion gitVersion, out string error)
        {
            gitVersion = new GitVersion(2, 17, 1, "gvfs", 1, 4);
            error = null;
            return true;
        }

        protected override bool IsServiceInstalledAndNotRunning()
        {
            this.SequenceTracker.RecordMethod("PreCheck_IsServiceRunning");
            return this.FakedResultOfCheck(FailOnCheckType.IsServiceInstalledAndNotRunning);
        }

        protected override bool IsElevated()
        {
            this.SequenceTracker.RecordMethod("PreCheck_Elevated");
            return this.FakedResultOfCheck(FailOnCheckType.IsElevated);
        }

        protected override bool IsProjFSInboxed()
        {
            this.SequenceTracker.RecordMethod("PreCheck_ProjFSInboxed");
            return this.FakedResultOfCheck(FailOnCheckType.ProjFSEnabled);
        }

        protected override bool IsUnattended()
        {
            this.SequenceTracker.RecordMethod("PreCheck_Unattended");
            return this.FakedResultOfCheck(FailOnCheckType.UnattendedMode);
        }

        protected override bool IsDevelopmentVersion()
        {
            this.SequenceTracker.RecordMethod("PreCheck_DevelopmentVersion");
            return this.FakedResultOfCheck(FailOnCheckType.IsDevelopmentVersion);
        }

        protected override bool IsBlockingProcessRunning(out List<string> processes)
        {
            this.SequenceTracker.RecordMethod("PreCheck_BlockingProcessRunning");

            processes = new List<string>();

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
            this.GVFSArgs.Add(args);
            
            if (string.CompareOrdinal(args, "service --unmount-all") == 0)
            {
                this.SequenceTracker.RecordMethod("UnmountAll");
                bool result = this.FakedResultOfCheck(FailOnCheckType.UnMountRepos);
                error = result == false ? "Unmount of some of the repositories failed." : null;
                return result;
            }

            if (string.CompareOrdinal(args, "service --mount-all") == 0)
            {
                this.SequenceTracker.RecordMethod("MountAll");
                bool result = this.FakedResultOfCheck(FailOnCheckType.RemountRepos);
                error = result == false ? "Auto remount failed." : null;
                return result;
            }

            error = "Unknown GVFS command";
            return false;
        }

        private bool FakedResultOfCheck(FailOnCheckType checkType)
        {
            bool result = this.failOnCheck.HasFlag(checkType) ? false : true;

            return result;
        }
    }
}
