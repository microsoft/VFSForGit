using GVFS.Common.Sparse;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;

namespace GVFS.UnitTests.Common.Sparse
{
    [TestFixture]
    public class SparseIndexGitSupportGuardTests
    {
        private const string ConeFile = "/*\n!/*/\n/A/\n";
        private const string SparseCheckoutPath = @"C:\repo\src\.git\info\sparse-checkout";

        [TestCase]
        public void NoSettingAndNoFileIsNotATrigger()
        {
            SparseIndexGitSupportGuard.GetTriggers(autoSparseIndex: false, coneFileStart: null)
                .ShouldEqual(SparseIndexGitSupportGuard.Triggers.None);
        }

        [TestCase]
        public void AutoSparseIndexIsATrigger()
        {
            SparseIndexGitSupportGuard.GetTriggers(autoSparseIndex: true, coneFileStart: null)
                .ShouldEqual(SparseIndexGitSupportGuard.Triggers.AutoSparseIndex);
        }



        [TestCase(ConeFile)]
        [TestCase("/*\r\n!/*/\r\n")]
        [TestCase("/*\n!/*/")]
        public void ConeFileIsATrigger(string content)
        {
            SparseIndexGitSupportGuard.GetTriggers(autoSparseIndex: false, coneFileStart: content)
                .ShouldEqual(SparseIndexGitSupportGuard.Triggers.ConeFile);
        }

        [TestCase("/.gitattributes\r\n")]
        [TestCase("")]
        [TestCase("/*\n")]
        [TestCase("!/*/\n/*\n")]
        public void LegacyOrEmptySparseCheckoutFileIsNotATrigger(string content)
        {
            // Versions of VFS for Git before 2018 wrote "/.gitattributes" to this file.
            SparseIndexGitSupportGuard.GetTriggers(autoSparseIndex: false, coneFileStart: content)
                .ShouldEqual(SparseIndexGitSupportGuard.Triggers.None);
        }

        [TestCase]
        public void AllTriggersAreReported()
        {
            SparseIndexGitSupportGuard.Triggers triggers = SparseIndexGitSupportGuard.GetTriggers(autoSparseIndex: true, coneFileStart: ConeFile);

            triggers.ShouldEqual(
                SparseIndexGitSupportGuard.Triggers.AutoSparseIndex |
                SparseIndexGitSupportGuard.Triggers.ConeFile);
            string message = SparseIndexGitSupportGuard.GetRefusalMessage(triggers, @"C:\Program Files\Git\cmd\git.exe", "2.56.1.vfs.0.1", SparseCheckoutPath);
            message.ShouldContain(
                "gvfs.auto-sparse-index is true",
                $"'{SparseCheckoutPath}' is a cone-mode sparse-checkout file");
        }

        [TestCase]
        public void RefusalMessageNamesTheGitAndTheRecoverySteps()
        {
            string message = SparseIndexGitSupportGuard.GetRefusalMessage(
                SparseIndexGitSupportGuard.Triggers.ConeFile,
                @"C:\Program Files\Git\cmd\git.exe",
                "2.56.1.vfs.0.1",
                SparseCheckoutPath);

            message.ShouldContain(
                @"'C:\Program Files\Git\cmd\git.exe', version 2.56.1.vfs.0.1",
                "'feature: vfs-sparse-index'",
                "git -c core.hookspath= config --unset-all gvfs.auto-sparse-index",
                "git -c core.hookspath= config --unset-all index.sparse",
                "git -c core.hookspath= config core.sparseCheckout false",
                $"del \"{SparseCheckoutPath}\"",
                "gvfs mount");
            message.ShouldNotContain(ignoreCase: false, "gvfs.auto-sparse-index is true");
        }

        [TestCase]
        public void RefusalMessageNeedsATrigger()
        {
            Assert.Throws<ArgumentException>(() => SparseIndexGitSupportGuard.GetRefusalMessage(SparseIndexGitSupportGuard.Triggers.None, "git", "1", SparseCheckoutPath));
        }
    }
}
