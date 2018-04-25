using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.Diagnostics;
using System.IO;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    [Category(Categories.FullSuiteOnly)]
    public class DehydrateTests : TestsWithEnlistmentPerFixture
    {
        private const int GVFSGenericError = 3;
        private FileSystemRunner fileSystem;

        // Set forcePerRepoObjectCache to true so that DehydrateShouldSucceedEvenIfObjectCacheIsDeleted does
        // not delete the shared local cache
        public DehydrateTests()
            : base(forcePerRepoObjectCache: true)
        {
            this.fileSystem = new SystemIORunner();
        }

        [TestCase]
        public void DehydrateShouldExitWithoutConfirm()
        {
            this.DehydrateShouldSucceed("To actually execute the dehydrate, run 'gvfs dehydrate --confirm'", confirm: false, noStatus: false);
        }

        [TestCase]
        public void DehydrateShouldSucceedInCommonCase()
        {
            this.DehydrateShouldSucceed("The repo was successfully dehydrated and remounted", confirm: true, noStatus: false);
        }

        [TestCase]
        public void DehydrateShouldFailOnUnmountedRepoWithStatus()
        {
            this.Enlistment.UnmountGVFS();
            this.DehydrateShouldFail("Failed to run git status because the repo is not mounted", noStatus: false);
            this.Enlistment.MountGVFS();
        }

        [TestCase]
        public void DehydrateShouldSucceedEvenIfObjectCacheIsDeleted()
        {
            this.Enlistment.UnmountGVFS();
            CmdRunner.DeleteDirectoryWithRetry(this.Enlistment.GetObjectRoot(this.fileSystem));
            this.DehydrateShouldSucceed("The repo was successfully dehydrated and remounted", confirm: true, noStatus: true);
        }

        [TestCase]
        public void DehydrateShouldFailIfLocalCacheNotInMetadata()
        {
            this.Enlistment.UnmountGVFS();

            string majorVersion;
            string minorVersion;
            GVFSHelpers.GetPersistedDiskLayoutVersion(this.Enlistment.DotGVFSRoot, out majorVersion, out minorVersion);
            string objectsRoot = GVFSHelpers.GetPersistedGitObjectsRoot(this.Enlistment.DotGVFSRoot).ShouldNotBeNull();

            string metadataPath = Path.Combine(this.Enlistment.DotGVFSRoot, GVFSHelpers.RepoMetadataName);
            string metadataBackupPath = metadataPath + ".backup";
            this.fileSystem.MoveFile(metadataPath, metadataBackupPath);

            this.fileSystem.CreateEmptyFile(metadataPath);
            GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, majorVersion, minorVersion);
            GVFSHelpers.SaveGitObjectsRoot(this.Enlistment.DotGVFSRoot, objectsRoot);

            this.DehydrateShouldFail("Failed to determine local cache path from repo metadata", noStatus: true);

            this.fileSystem.DeleteFile(metadataPath);
            this.fileSystem.MoveFile(metadataBackupPath, metadataPath);

            this.Enlistment.MountGVFS();
        }

        [TestCase]
        public void DehydrateShouldFailIfGitObjectsRootNotInMetadata()
        {
            this.Enlistment.UnmountGVFS();

            string majorVersion;
            string minorVersion;
            GVFSHelpers.GetPersistedDiskLayoutVersion(this.Enlistment.DotGVFSRoot, out majorVersion, out minorVersion);
            string localCacheRoot = GVFSHelpers.GetPersistedLocalCacheRoot(this.Enlistment.DotGVFSRoot).ShouldNotBeNull();

            string metadataPath = Path.Combine(this.Enlistment.DotGVFSRoot, GVFSHelpers.RepoMetadataName);
            string metadataBackupPath = metadataPath + ".backup";
            this.fileSystem.MoveFile(metadataPath, metadataBackupPath);

            this.fileSystem.CreateEmptyFile(metadataPath);
            GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, majorVersion, minorVersion);
            GVFSHelpers.SaveLocalCacheRoot(this.Enlistment.DotGVFSRoot, localCacheRoot);

            this.DehydrateShouldFail("Failed to determine git objects root from repo metadata", noStatus: true);

            this.fileSystem.DeleteFile(metadataPath);
            this.fileSystem.MoveFile(metadataBackupPath, metadataPath);

            this.Enlistment.MountGVFS();
        }

        [TestCase]
        public void DehydrateShouldFailOnWrongDiskLayoutVersion()
        {
            this.Enlistment.UnmountGVFS();

            string majorVersion;
            string minorVersion;
            GVFSHelpers.GetPersistedDiskLayoutVersion(this.Enlistment.DotGVFSRoot, out majorVersion, out minorVersion);

            int majorVersionNum;
            int minorVersionNum;
            int.TryParse(majorVersion.ShouldNotBeNull(), out majorVersionNum).ShouldEqual(true);
            int.TryParse(minorVersion.ShouldNotBeNull(), out minorVersionNum).ShouldEqual(true);

            GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, (majorVersionNum - 1).ToString(), "0");
            this.DehydrateShouldFail("disk layout version doesn't match current version", noStatus: true);

            GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, (majorVersionNum + 1).ToString(), "0");
            this.DehydrateShouldFail("Changes to GVFS disk layout do not allow mounting after downgrade.", noStatus: true);

            GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, majorVersionNum.ToString(), minorVersionNum.ToString());

            this.Enlistment.MountGVFS();
        }

        private void DehydrateShouldSucceed(string expectedOutput, bool confirm, bool noStatus)
        {
            ProcessResult result = this.RunDehydrateProcess(confirm, noStatus);
            result.ExitCode.ShouldEqual(0, $"mount exit code was {result.ExitCode}. Output: {result.Output}");
            result.Output.ShouldContain(expectedOutput);
        }

        private void DehydrateShouldFail(string expectedErrorMessage, bool noStatus)
        {
            ProcessResult result = this.RunDehydrateProcess(confirm: true, noStatus: noStatus);
            result.ExitCode.ShouldEqual(GVFSGenericError, $"mount exit code was not {GVFSGenericError}");
            result.Output.ShouldContain(expectedErrorMessage);
        }

        private ProcessResult RunDehydrateProcess(bool confirm, bool noStatus)
        {
            string dehydrateFlags = string.Empty;
            if (confirm)
            {
                dehydrateFlags += " --confirm ";
            }

            if (noStatus)
            {
                dehydrateFlags += " --no-status ";
            }

            string enlistmentRoot = this.Enlistment.EnlistmentRoot;

            ProcessStartInfo processInfo = new ProcessStartInfo(GVFSTestConfig.PathToGVFS);
            processInfo.Arguments = "dehydrate " + dehydrateFlags + " --internal_use_only_service_name " + GVFSServiceProcess.TestServiceName;
            processInfo.WindowStyle = ProcessWindowStyle.Hidden;
            processInfo.WorkingDirectory = enlistmentRoot;
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardOutput = true;

            return ProcessHelper.Run(processInfo);
        }
    }
}