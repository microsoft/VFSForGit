using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Properties;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using Microsoft.Win32.SafeHandles;
using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    [Category(Categories.ExtraCoverage)]
    public class MountTests : TestsWithEnlistmentPerFixture
    {
        private const int GVFSGenericError = 3;
        private const uint GenericRead = 2147483648;
        private const uint FileFlagBackupSemantics = 3355443;
        private readonly int fileDeletedBackgroundOperationCode;
        private readonly int directoryDeletedBackgroundOperationCode;

        private FileSystemRunner fileSystem;

        public MountTests()
        {
            this.fileSystem = new SystemIORunner();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                this.fileDeletedBackgroundOperationCode = 16;
                this.directoryDeletedBackgroundOperationCode = 17;
            }
            else
            {
                this.fileDeletedBackgroundOperationCode = 3;
                this.directoryDeletedBackgroundOperationCode = 11;
            }
        }

        [TestCaseSource(typeof(MountSubfolders), MountSubfolders.MountFolders)]
        public void SecondMountAttemptFails(string mountSubfolder)
        {
            this.MountShouldFail(0, "already mounted", this.Enlistment.GetVirtualPathTo(mountSubfolder));
        }

        [TestCase]
        public void MountFailsOutsideEnlistment()
        {
            this.MountShouldFail("is not a valid GVFS enlistment", Path.GetDirectoryName(this.Enlistment.EnlistmentRoot));
        }

        [TestCase]
        public void MountCopiesMissingReadObjectHook()
        {
            this.Enlistment.UnmountGVFS();

            string readObjectPath = this.Enlistment.GetDotGitPath("hooks", "read-object" + Settings.Default.BinaryFileNameExtension);
            readObjectPath.ShouldBeAFile(this.fileSystem);
            this.fileSystem.DeleteFile(readObjectPath);
            readObjectPath.ShouldNotExistOnDisk(this.fileSystem);
            this.Enlistment.MountGVFS();
            readObjectPath.ShouldBeAFile(this.fileSystem);
        }

        [TestCase]
        public void MountSetsCoreHooksPath()
        {
            try
            {
                GVFSHelpers.RegisterForOfflineIO();

                this.Enlistment.UnmountGVFS();

                GitProcess.Invoke(this.Enlistment.RepoRoot, "config --unset core.hookspath");
                string.IsNullOrWhiteSpace(
                    GitProcess.Invoke(this.Enlistment.RepoRoot, "config core.hookspath"))
                    .ShouldBeTrue();

                this.Enlistment.MountGVFS();
                string expectedHooksPath = this.Enlistment.GetDotGitPath("hooks");
                expectedHooksPath = GitHelpers.ConvertPathToGitFormat(expectedHooksPath);

                GitProcess.Invoke(
                    this.Enlistment.RepoRoot, "config core.hookspath")
                    .Trim('\n')
                    .ShouldEqual(expectedHooksPath);
            }
            finally
            {
                GVFSHelpers.UnregisterForOfflineIO();
            }
        }

        [TestCase]
        [Category(Categories.WindowsOnly)] // Only Windows uses GitHooksLoader.exe and merges hooks
        public void MountMergesLocalPrePostHooksConfig()
        {
            // Create some dummy pre/post command hooks
            string dummyCommandHookBin = "cmd.exe /c exit 0";

            // Confirm git is not already using the dummy hooks
            string localGitPreCommandHooks = this.Enlistment.GetVirtualPathTo(".git", "hooks", "pre-command.hooks");
            localGitPreCommandHooks.ShouldBeAFile(this.fileSystem).WithContents().Contains(dummyCommandHookBin).ShouldBeFalse();

            string localGitPostCommandHooks = this.Enlistment.GetVirtualPathTo(".git", "hooks", "post-command.hooks");
            localGitPreCommandHooks.ShouldBeAFile(this.fileSystem).WithContents().Contains(dummyCommandHookBin).ShouldBeFalse();

            this.Enlistment.UnmountGVFS();

            // Create dummy-<pre/post>-command.hooks and set them in the local git config
            string dummyPreCommandHooksConfig = Path.Combine(this.Enlistment.EnlistmentRoot, "dummy-pre-command.hooks");
            this.fileSystem.WriteAllText(dummyPreCommandHooksConfig, dummyCommandHookBin);
            string dummyOostCommandHooksConfig = Path.Combine(this.Enlistment.EnlistmentRoot, "dummy-post-command.hooks");
            this.fileSystem.WriteAllText(dummyOostCommandHooksConfig, dummyCommandHookBin);

            // Configure the hooks locally
            GitProcess.Invoke(this.Enlistment.RepoRoot, $"config gvfs.clone.default-pre-command {dummyPreCommandHooksConfig}");
            GitProcess.Invoke(this.Enlistment.RepoRoot, $"config gvfs.clone.default-post-command {dummyOostCommandHooksConfig}");

            // Mount the repo
            this.Enlistment.MountGVFS();

            // .git\hooks\<pre/post>-command.hooks should now contain our local dummy hook
            // The dummy pre-command hooks should appear first, and the post-command hook should appear last
            string[] mergedPreCommandHooksLines = localGitPreCommandHooks
                .ShouldBeAFile(this.fileSystem)
                .WithContents()
                .Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            mergedPreCommandHooksLines.Length.ShouldEqual(2, $"Expected 2 lines, actual: {string.Join("\n", mergedPreCommandHooksLines)}");
            mergedPreCommandHooksLines[0].ShouldEqual(dummyCommandHookBin);

            string[] mergedPostCommandHooksLines = localGitPostCommandHooks
                .ShouldBeAFile(this.fileSystem)
                .WithContents()
                .Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            mergedPreCommandHooksLines.Length.ShouldEqual(2, $"Expected 2 lines, actual: {string.Join("\n", mergedPostCommandHooksLines)}");
            mergedPreCommandHooksLines[1].ShouldEqual(dummyCommandHookBin);
        }

        [TestCase]
        public void MountChangesMountId()
        {
            string mountId = GitProcess.Invoke(this.Enlistment.RepoRoot, "config gvfs.mount-id")
                .Trim('\n');
            this.Enlistment.UnmountGVFS();
            this.Enlistment.MountGVFS();
            GitProcess.Invoke(this.Enlistment.RepoRoot, "config gvfs.mount-id")
                .Trim('\n')
                .ShouldNotEqual(mountId, "gvfs.mount-id should change on every mount");
        }

        [TestCase]
        public void MountFailsWhenNoOnDiskVersion()
        {
            this.Enlistment.UnmountGVFS();

            // Get the current disk layout version
            string majorVersion;
            string minorVersion;
            GVFSHelpers.GetPersistedDiskLayoutVersion(this.Enlistment.DotGVFSRoot, out majorVersion, out minorVersion);

            int majorVersionNum;
            int minorVersionNum;
            int.TryParse(majorVersion.ShouldNotBeNull(), out majorVersionNum).ShouldEqual(true);
            int.TryParse(minorVersion.ShouldNotBeNull(), out minorVersionNum).ShouldEqual(true);

            // Move the RepoMetadata database to a temp file
            string versionDatabasePath = Path.Combine(this.Enlistment.DotGVFSRoot, GVFSHelpers.RepoMetadataName);
            versionDatabasePath.ShouldBeAFile(this.fileSystem);

            string tempDatabasePath = versionDatabasePath + "_MountFailsWhenNoOnDiskVersion";
            tempDatabasePath.ShouldNotExistOnDisk(this.fileSystem);

            this.fileSystem.MoveFile(versionDatabasePath, tempDatabasePath);
            versionDatabasePath.ShouldNotExistOnDisk(this.fileSystem);

            this.MountShouldFail("Failed to upgrade repo disk layout");

            // Move the RepoMetadata database back
            this.fileSystem.DeleteFile(versionDatabasePath);
            this.fileSystem.MoveFile(tempDatabasePath, versionDatabasePath);
            tempDatabasePath.ShouldNotExistOnDisk(this.fileSystem);
            versionDatabasePath.ShouldBeAFile(this.fileSystem);

            this.Enlistment.MountGVFS();
        }

        [TestCase]
        public void MountFailsWhenNoLocalCacheRootInRepoMetadata()
        {
            this.Enlistment.UnmountGVFS();

            string majorVersion;
            string minorVersion;
            GVFSHelpers.GetPersistedDiskLayoutVersion(this.Enlistment.DotGVFSRoot, out majorVersion, out minorVersion);
            majorVersion.ShouldNotBeNull();
            minorVersion.ShouldNotBeNull();

            string objectsRoot = GVFSHelpers.GetPersistedGitObjectsRoot(this.Enlistment.DotGVFSRoot).ShouldNotBeNull();

            string metadataPath = Path.Combine(this.Enlistment.DotGVFSRoot, GVFSHelpers.RepoMetadataName);
            string metadataBackupPath = metadataPath + ".backup";
            this.fileSystem.MoveFile(metadataPath, metadataBackupPath);

            this.fileSystem.CreateEmptyFile(metadataPath);
            GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, majorVersion, minorVersion);
            GVFSHelpers.SaveGitObjectsRoot(this.Enlistment.DotGVFSRoot, objectsRoot);

            this.MountShouldFail("Failed to determine local cache path from repo metadata");

            this.fileSystem.DeleteFile(metadataPath);
            this.fileSystem.MoveFile(metadataBackupPath, metadataPath);

            this.Enlistment.MountGVFS();
        }

        [TestCase]
        public void MountFailsWhenNoGitObjectsRootInRepoMetadata()
        {
            this.Enlistment.UnmountGVFS();

            string majorVersion;
            string minorVersion;
            GVFSHelpers.GetPersistedDiskLayoutVersion(this.Enlistment.DotGVFSRoot, out majorVersion, out minorVersion);
            majorVersion.ShouldNotBeNull();
            minorVersion.ShouldNotBeNull();

            string localCacheRoot = GVFSHelpers.GetPersistedLocalCacheRoot(this.Enlistment.DotGVFSRoot).ShouldNotBeNull();

            string metadataPath = Path.Combine(this.Enlistment.DotGVFSRoot, GVFSHelpers.RepoMetadataName);
            string metadataBackupPath = metadataPath + ".backup";
            this.fileSystem.MoveFile(metadataPath, metadataBackupPath);

            this.fileSystem.CreateEmptyFile(metadataPath);
            GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, majorVersion, minorVersion);
            GVFSHelpers.SaveLocalCacheRoot(this.Enlistment.DotGVFSRoot, localCacheRoot);

            this.MountShouldFail("Failed to determine git objects root from repo metadata");

            this.fileSystem.DeleteFile(metadataPath);
            this.fileSystem.MoveFile(metadataBackupPath, metadataPath);

            this.Enlistment.MountGVFS();
        }

        [TestCase]
        public void MountRegeneratesAlternatesFileWhenMissingGitObjectsRoot()
        {
            this.Enlistment.UnmountGVFS();

            string objectsRoot = GVFSHelpers.GetPersistedGitObjectsRoot(this.Enlistment.DotGVFSRoot).ShouldNotBeNull();

            string alternatesFilePath = this.Enlistment.GetDotGitPath("objects", "info", "alternates");
            alternatesFilePath.ShouldBeAFile(this.fileSystem).WithContents(objectsRoot);
            this.fileSystem.WriteAllText(alternatesFilePath, "Z:\\invalidPath");

            this.Enlistment.MountGVFS();

            alternatesFilePath.ShouldBeAFile(this.fileSystem).WithContents(objectsRoot);
        }

        [TestCase]
        public void MountRegeneratesAlternatesFileWhenMissingFromDisk()
        {
            this.Enlistment.UnmountGVFS();

            string objectsRoot = GVFSHelpers.GetPersistedGitObjectsRoot(this.Enlistment.DotGVFSRoot).ShouldNotBeNull();

            string alternatesFilePath = this.Enlistment.GetDotGitPath("objects", "info", "alternates");
            alternatesFilePath.ShouldBeAFile(this.fileSystem).WithContents(objectsRoot);
            this.fileSystem.DeleteFile(alternatesFilePath);

            this.Enlistment.MountGVFS();

            alternatesFilePath.ShouldBeAFile(this.fileSystem).WithContents(objectsRoot);
        }

        [TestCase]
        public void MountCanProcessSavedBackgroundQueueTasks()
        {
            string deletedFileEntry = "Test_EPF_WorkingDirectoryTests/1/2/3/4/ReadDeepProjectedFile.cpp";
            string deletedDirEntry = "Test_EPF_WorkingDirectoryTests/1/2/3/4/";
            GVFSHelpers.ModifiedPathsShouldNotContain(this.Enlistment, this.fileSystem, deletedFileEntry);
            GVFSHelpers.ModifiedPathsShouldNotContain(this.Enlistment, this.fileSystem, deletedDirEntry);
            this.Enlistment.UnmountGVFS();

            // Prime the background queue with delete messages
            string deleteFilePath = Path.Combine("Test_EPF_WorkingDirectoryTests", "1", "2", "3", "4", "ReadDeepProjectedFile.cpp");
            string deleteDirPath = Path.Combine("Test_EPF_WorkingDirectoryTests", "1", "2", "3", "4");
            string persistedDeleteFileTask = $"A 1\0{this.fileDeletedBackgroundOperationCode}\0{deleteFilePath}\0";
            string persistedDeleteDirectoryTask = $"A 2\0{this.directoryDeletedBackgroundOperationCode}\0{deleteDirPath}\0";
            this.fileSystem.WriteAllText(
                Path.Combine(this.Enlistment.EnlistmentRoot, GVFSTestConfig.DotGVFSRoot, "databases", "BackgroundGitOperations.dat"),
                $"{persistedDeleteFileTask}\r\n{persistedDeleteDirectoryTask}\r\n");

            // Background queue should process the delete messages and modifiedPaths should show the change
            this.Enlistment.MountGVFS();
            this.Enlistment.WaitForBackgroundOperations();
            GVFSHelpers.ModifiedPathsShouldContain(this.Enlistment, this.fileSystem, deletedFileEntry);
            GVFSHelpers.ModifiedPathsShouldContain(this.Enlistment, this.fileSystem, deletedDirEntry);
        }

        [TestCase]
        public void MountingARepositoryThatRequiresPlaceholderUpdatesWorks()
        {
            string placeholderRelativePath = Path.Combine("EnumerateAndReadTestFiles", "a.txt");
            string placeholderPath = this.Enlistment.GetVirtualPathTo(placeholderRelativePath);

            // Ensure the placeholder is on disk and hydrated
            placeholderPath.ShouldBeAFile(this.fileSystem).WithContents();

            this.Enlistment.UnmountGVFS();

            File.Delete(placeholderPath);
            GVFSHelpers.DeletePlaceholder(
                Path.Combine(this.Enlistment.DotGVFSRoot, TestConstants.Databases.VFSForGit),
                placeholderRelativePath);
            GVFSHelpers.SetPlaceholderUpdatesRequired(this.Enlistment.DotGVFSRoot, true);

            this.Enlistment.MountGVFS();
        }

        [TestCaseSource(typeof(MountSubfolders), MountSubfolders.MountFolders)]
        public void MountFailsAfterBreakingDowngrade(string mountSubfolder)
        {
            MountSubfolders.EnsureSubfoldersOnDisk(this.Enlistment, this.fileSystem);
            this.Enlistment.UnmountGVFS();

            string majorVersion;
            string minorVersion;
            GVFSHelpers.GetPersistedDiskLayoutVersion(this.Enlistment.DotGVFSRoot, out majorVersion, out minorVersion);

            int majorVersionNum;
            int minorVersionNum;
            int.TryParse(majorVersion.ShouldNotBeNull(), out majorVersionNum).ShouldEqual(true);
            int.TryParse(minorVersion.ShouldNotBeNull(), out minorVersionNum).ShouldEqual(true);

            GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, (majorVersionNum + 1).ToString(), "0");

            this.MountShouldFail("do not allow mounting after downgrade", this.Enlistment.GetVirtualPathTo(mountSubfolder));

            GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, majorVersionNum.ToString(), minorVersionNum.ToString());
            this.Enlistment.MountGVFS();
        }

        [TestCaseSource(typeof(MountSubfolders), MountSubfolders.MountFolders)]
        public void MountFailsUpgradingFromInvalidUpgradePath(string mountSubfolder)
        {
            MountSubfolders.EnsureSubfoldersOnDisk(this.Enlistment, this.fileSystem);
            string headCommitId = GitProcess.Invoke(this.Enlistment.RepoRoot, "rev-parse HEAD");

            this.Enlistment.UnmountGVFS();

            string majorVersion;
            string minorVersion;
            GVFSHelpers.GetPersistedDiskLayoutVersion(this.Enlistment.DotGVFSRoot, out majorVersion, out minorVersion);

            int majorVersionNum;
            int minorVersionNum;
            int.TryParse(majorVersion.ShouldNotBeNull(), out majorVersionNum).ShouldEqual(true);
            int.TryParse(minorVersion.ShouldNotBeNull(), out minorVersionNum).ShouldEqual(true);

            // 1 will always be below the minumum support version number
            GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, "1", "0");
            this.MountShouldFail("Breaking change to GVFS disk layout has been made since cloning", this.Enlistment.GetVirtualPathTo(mountSubfolder));

            GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, majorVersionNum.ToString(), minorVersionNum.ToString());
            this.Enlistment.MountGVFS();
        }

        // Ported from ProjFS's BugRegressionTest
        [TestCase]
        [Category(Categories.WindowsOnly)]
        public void ProjFS_CMDHangNoneActiveInstance()
        {
            this.Enlistment.UnmountGVFS();

            using (SafeFileHandle handle = NativeMethods.CreateFile(
                Path.Combine(this.Enlistment.RepoRoot, "aaa", "aaaa"),
                GenericRead,
                FileShare.Read,
                IntPtr.Zero,
                FileMode.Open,
                FileFlagBackupSemantics,
                IntPtr.Zero))
            {
                int lastError = Marshal.GetLastWin32Error();
                handle.IsInvalid.ShouldEqual(true);
                lastError.ShouldNotEqual(0); // 0 == ERROR_SUCCESS
            }

            this.Enlistment.MountGVFS();
        }

        private void MountShouldFail(int expectedExitCode, string expectedErrorMessage, string mountWorkingDirectory = null)
        {
            string enlistmentRoot = this.Enlistment.EnlistmentRoot;

            // TODO: 865304 Use app.config instead of --internal* arguments
            ProcessStartInfo processInfo = new ProcessStartInfo(GVFSTestConfig.PathToGVFS);
            processInfo.Arguments = "mount " + TestConstants.InternalUseOnlyFlag + " " + GVFSHelpers.GetInternalParameter();
            processInfo.WindowStyle = ProcessWindowStyle.Hidden;
            processInfo.WorkingDirectory = string.IsNullOrEmpty(mountWorkingDirectory) ? enlistmentRoot : mountWorkingDirectory;
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardOutput = true;

            ProcessResult result = ProcessHelper.Run(processInfo);
            result.ExitCode.ShouldEqual(expectedExitCode, $"mount exit code was not {expectedExitCode}. Output: {result.Output}");
            result.Output.ShouldContain(expectedErrorMessage);
        }

        private void MountShouldFail(string expectedErrorMessage, string mountWorkingDirectory = null)
        {
            this.MountShouldFail(GVFSGenericError, expectedErrorMessage, mountWorkingDirectory);
        }

        private class MountSubfolders
        {
            public const string MountFolders = "Folders";

            public static object[] Folders
            {
                get
                {
                    // On Linux, an unmounted repository is completely empty, so we must
                    // only try to mount from the root of the virtual path.

                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    {
                        return new object[] { new object[] { string.Empty } };
                    }
                    else
                    {
                        return new object[]
                        {
                            new object[] { string.Empty },
                            new object[] { "GVFS" },
                        };
                    }
                }
            }

            public static void EnsureSubfoldersOnDisk(GVFSFunctionalTestEnlistment enlistment, FileSystemRunner fileSystem)
            {
                // Enumerate the directory to ensure that the folder is on disk after GVFS is unmounted
                foreach (object[] folder in Folders)
                {
                    string folderPath = enlistment.GetVirtualPathTo((string)folder[0]);
                    folderPath.ShouldBeADirectory(fileSystem).WithItems();
                }
            }
        }
    }
}
