using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    [Category(Categories.ExtraCoverage)]
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
            RepositoryHelpers.DeleteTestDirectory(this.Enlistment.GetObjectRoot(this.fileSystem));
            this.DehydrateShouldSucceed("The repo was successfully dehydrated and remounted", confirm: true, noStatus: true);
        }

        [TestCase]
        public void DehydrateShouldBackupFiles()
        {
            this.DehydrateShouldSucceed("The repo was successfully dehydrated and remounted", confirm: true, noStatus: false);
            string backupFolder = Path.Combine(this.Enlistment.EnlistmentRoot, "dehydrate_backup");
            backupFolder.ShouldBeADirectory(this.fileSystem);
            string[] backupFolderItems = this.fileSystem.EnumerateDirectory(backupFolder).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            backupFolderItems.Length.ShouldEqual(1);
            this.DirectoryShouldContain(backupFolderItems[0], ".git", GVFSTestConfig.DotGVFSRoot, "src");

            // .git folder items
            string gitFolder = Path.Combine(backupFolderItems[0], ".git");
            this.DirectoryShouldContain(gitFolder, "index");

            // .gvfs folder items
            string gvfsFolder = Path.Combine(backupFolderItems[0], GVFSTestConfig.DotGVFSRoot);
            this.DirectoryShouldContain(gvfsFolder, "databases", "GVFS_projection");

            string gvfsDatabasesFolder = Path.Combine(gvfsFolder, "databases");
            this.DirectoryShouldContain(gvfsDatabasesFolder, "BackgroundGitOperations.dat", "ModifiedPaths.dat", "VFSForGit.sqlite");
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

        private void DirectoryShouldContain(string directory, params string[] fileOrFolders)
        {
            IEnumerable<string> onDiskItems =
                this.fileSystem.EnumerateDirectory(directory)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(path => Path.GetFileName(path))
                .OrderByDescending(x => x);

            onDiskItems.ShouldMatchInOrder(fileOrFolders.OrderByDescending(x => x));
        }

        private void DehydrateShouldSucceed(string expectedOutput, bool confirm, bool noStatus)
        {
            ProcessResult result = this.RunDehydrateProcess(confirm, noStatus);
            result.ExitCode.ShouldEqual(0, $"mount exit code was {result.ExitCode}. Output: {result.Output}");

            if (result.Output.Contains("Failed to move the src folder: Access to the path"))
            {
                string output = this.RunHandleProcess(Path.Combine(this.Enlistment.EnlistmentRoot, "src"));
                TestContext.Out.WriteLine(output);
            }

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
            processInfo.Arguments = "dehydrate " + dehydrateFlags + " " + TestConstants.InternalUseOnlyFlag + " " + GVFSHelpers.GetInternalParameter();
            processInfo.WindowStyle = ProcessWindowStyle.Hidden;
            processInfo.WorkingDirectory = enlistmentRoot;
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardOutput = true;

            return ProcessHelper.Run(processInfo);
        }

        private string RunHandleProcess(string path)
        {
            try
            {
                ProcessStartInfo processInfo = new ProcessStartInfo("handle.exe");
                processInfo.Arguments = "-p " + path;
                processInfo.WindowStyle = ProcessWindowStyle.Hidden;
                processInfo.WorkingDirectory = this.Enlistment.EnlistmentRoot;
                processInfo.UseShellExecute = false;
                processInfo.RedirectStandardOutput = true;

                return "handle.exe output: " + ProcessHelper.Run(processInfo).Output;
            }
            catch (Exception ex)
            {
                return $"Exception running handle.exe - {ex.Message}";
            }
        }
    }
}
