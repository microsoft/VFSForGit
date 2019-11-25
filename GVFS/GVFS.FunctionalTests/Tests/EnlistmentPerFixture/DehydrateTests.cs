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
using System.Runtime.InteropServices;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    [Category(Categories.ExtraCoverage)]
    public class DehydrateTests : TestsWithEnlistmentPerFixture
    {
        private const string FolderDehydrateSuccessfulMessage = "folder dehydrate successful.";
        private const int GVFSGenericError = 3;
        private FileSystemRunner fileSystem;

        // Set forcePerRepoObjectCache to true so that DehydrateShouldSucceedEvenIfObjectCacheIsDeleted does
        // not delete the shared local cache
        public DehydrateTests()
            : base(forcePerRepoObjectCache: true)
        {
            this.fileSystem = new SystemIORunner();
        }

        [TearDown]
        public void TearDown()
        {
            string backupFolder = Path.Combine(this.Enlistment.EnlistmentRoot, "dehydrate_backup");
            if (this.fileSystem.DirectoryExists(backupFolder))
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    // Mac gets permission denied when using the System.IO DeleteDirectory
                    BashRunner runner = new BashRunner();
                    runner.DeleteDirectory(backupFolder);
                }
                else
                {
                    this.fileSystem.DeleteDirectory(backupFolder);
                }
            }

            if (!this.Enlistment.IsMounted())
            {
                this.Enlistment.MountGVFS();
            }
        }

        [TestCase]
        public void DehydrateShouldExitWithoutConfirm()
        {
            this.DehydrateShouldSucceed(new[] { "To actually execute the dehydrate, run 'gvfs dehydrate --confirm'" }, confirm: false, noStatus: false);
        }

        [TestCase]
        public void DehydrateShouldSucceedInCommonCase()
        {
            this.DehydrateShouldSucceed(new[] { "The repo was successfully dehydrated and remounted" }, confirm: true, noStatus: false);
        }

        [TestCase]
        public void DehydrateShouldFailOnUnmountedRepoWithStatus()
        {
            this.Enlistment.UnmountGVFS();
            this.DehydrateShouldFail(new[] { "Failed to run git status because the repo is not mounted" }, noStatus: false);
        }

        [TestCase]
        public void DehydrateShouldSucceedEvenIfObjectCacheIsDeleted()
        {
            this.Enlistment.UnmountGVFS();
            RepositoryHelpers.DeleteTestDirectory(this.Enlistment.GetObjectRoot(this.fileSystem));
            this.DehydrateShouldSucceed(new[] { "The repo was successfully dehydrated and remounted" }, confirm: true, noStatus: true);
        }

        [TestCase]
        public void DehydrateShouldBackupFiles()
        {
            this.DehydrateShouldSucceed(new[] { "The repo was successfully dehydrated and remounted" }, confirm: true, noStatus: false);
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

            this.DehydrateShouldFail(new[] { "Failed to determine local cache path from repo metadata" }, noStatus: true);

            this.fileSystem.DeleteFile(metadataPath);
            this.fileSystem.MoveFile(metadataBackupPath, metadataPath);
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

            this.DehydrateShouldFail(new[] { "Failed to determine git objects root from repo metadata" }, noStatus: true);

            this.fileSystem.DeleteFile(metadataPath);
            this.fileSystem.MoveFile(metadataBackupPath, metadataPath);
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

            int previousMajorVersionNum = majorVersionNum - 1;
            if (previousMajorVersionNum >= GVFSHelpers.GetCurrentDiskLayoutMinimumMajorVersion())
            {
                GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, previousMajorVersionNum.ToString(), "0");
                this.DehydrateShouldFail(new[] { "disk layout version doesn't match current version" }, noStatus: true);
            }

            GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, (majorVersionNum + 1).ToString(), "0");
            this.DehydrateShouldFail(new[] { "Changes to GVFS disk layout do not allow mounting after downgrade." }, noStatus: true);

            GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, majorVersionNum.ToString(), minorVersionNum.ToString());
        }

        [TestCase]
        public void FolderDehydrateFolderThatWasEnumerated()
        {
            string folderToDehydrate = "GVFS";
            string folderToEnumerateVirtualPath = this.Enlistment.GetVirtualPathTo(folderToDehydrate);
            string folderToEnumerateBackingPath = this.Enlistment.GetBackingPathTo(folderToDehydrate);
            this.fileSystem.EnumerateDirectory(folderToEnumerateVirtualPath);
            string subFolderToEnumerate = Path.Combine(folderToDehydrate, "GVFS");
            string subFolderToEnumerateVirtualPath = this.Enlistment.GetVirtualPathTo(subFolderToEnumerate);
            string subFolderToEnumerateBackingPath = this.Enlistment.GetBackingPathTo(subFolderToEnumerate);
            this.fileSystem.EnumerateDirectory(subFolderToEnumerateVirtualPath);

            this.DehydrateShouldSucceed(new[] { $"{folderToDehydrate} {FolderDehydrateSuccessfulMessage}" }, confirm: true, noStatus: false, foldersToDehydrate: folderToDehydrate);
            this.Enlistment.UnmountGVFS();
            this.CheckDehydratedFolderAfterUnmount(folderToEnumerateBackingPath);
            subFolderToEnumerateBackingPath.ShouldNotExistOnDisk(this.fileSystem);
        }

        [TestCase]
        public void FolderDehydrateFolderWithFilesThatWerePlaceholders()
        {
            string folderToDehydrate = "GVFS";
            string folderToReadFilesVirtualPath = this.Enlistment.GetVirtualPathTo(folderToDehydrate);
            string folderToReadFilesBackingPath = this.Enlistment.GetBackingPathTo(folderToDehydrate);
            string fileToRead = Path.Combine(folderToDehydrate, "GVFS", "Program.cs");
            string fileToReadVirtualPath = this.Enlistment.GetVirtualPathTo(fileToRead);
            string fileToReadBackingPath = this.Enlistment.GetBackingPathTo(fileToRead);
            using (File.OpenRead(fileToReadVirtualPath))
            {
            }

            this.DehydrateShouldSucceed(new[] { $"{folderToDehydrate} {FolderDehydrateSuccessfulMessage}" }, confirm: true, noStatus: false, foldersToDehydrate: folderToDehydrate);
            this.Enlistment.UnmountGVFS();
            this.CheckDehydratedFolderAfterUnmount(folderToReadFilesBackingPath);
            fileToReadBackingPath.ShouldNotExistOnDisk(this.fileSystem);
        }

        [TestCase]
        public void FolderDehydrateFolderWithFilesThatWereRead()
        {
            string folderToDehydrate = "GVFS";
            string folderToReadFilesVirtualPath = this.Enlistment.GetVirtualPathTo(folderToDehydrate);
            string folderToReadFilesBackingPath = this.Enlistment.GetBackingPathTo(folderToDehydrate);
            string fileToRead = Path.Combine(folderToDehydrate, "GVFS", "Program.cs");
            string fileToReadVirtualPath = this.Enlistment.GetVirtualPathTo(fileToRead);
            string fileToReadBackingPath = this.Enlistment.GetBackingPathTo(fileToRead);
            this.fileSystem.ReadAllText(fileToReadVirtualPath);

            this.fileSystem.EnumerateDirectory(folderToReadFilesVirtualPath);

            this.DehydrateShouldSucceed(new[] { $"{folderToDehydrate} {FolderDehydrateSuccessfulMessage}" }, confirm: true, noStatus: false, foldersToDehydrate: folderToDehydrate);
            this.Enlistment.UnmountGVFS();
            this.CheckDehydratedFolderAfterUnmount(folderToReadFilesBackingPath);
            fileToReadBackingPath.ShouldNotExistOnDisk(this.fileSystem);
        }

        [TestCase]
        public void FolderDehydrateFolderWithFilesThatWereWrittenTo()
        {
            string folderToDehydrate = "GVFS";
            string folderToWriteFilesVirtualPath = this.Enlistment.GetVirtualPathTo(folderToDehydrate);
            string folderToWriteFilesBackingPath = this.Enlistment.GetBackingPathTo(folderToDehydrate);
            string fileToWrite = Path.Combine(folderToDehydrate, "GVFS", "Program.cs");
            string fileToWriteVirtualPath = this.Enlistment.GetVirtualPathTo(fileToWrite);
            string fileToWriteBackingPath = this.Enlistment.GetBackingPathTo(fileToWrite);
            this.fileSystem.AppendAllText(fileToWriteVirtualPath, "Append content");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "add .");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "commit -m Test");

            this.DehydrateShouldSucceed(new[] { $"{folderToDehydrate} {FolderDehydrateSuccessfulMessage}" }, confirm: true, noStatus: false, foldersToDehydrate: folderToDehydrate);
            this.Enlistment.UnmountGVFS();
            this.CheckDehydratedFolderAfterUnmount(folderToWriteFilesBackingPath);
            fileToWriteBackingPath.ShouldNotExistOnDisk(this.fileSystem);
        }

        [TestCase]
        public void FolderDehydrateFolderThatWasDeleted()
        {
            string folderToDehydrate = "Scripts";
            string folderToDeleteVirtualPath = this.Enlistment.GetVirtualPathTo(folderToDehydrate);
            string folderToDeleteBackingPath = this.Enlistment.GetBackingPathTo(folderToDehydrate);
            this.fileSystem.DeleteDirectory(folderToDeleteVirtualPath);
            GitProcess.Invoke(this.Enlistment.RepoRoot, $"checkout -- {folderToDehydrate}");

            this.DehydrateShouldSucceed(new[] { $"{folderToDehydrate} {FolderDehydrateSuccessfulMessage}" }, confirm: true, noStatus: false, foldersToDehydrate: folderToDehydrate);
            this.Enlistment.UnmountGVFS();
            this.CheckDehydratedFolderAfterUnmount(folderToDeleteBackingPath);
            Path.Combine(folderToDeleteBackingPath, "RunUnitTests.bat").ShouldNotExistOnDisk(this.fileSystem);
        }

        [TestCase]
        public void FolderDehydrateFolderThatIsSubstringOfExistingFolder()
        {
            string folderToDehydrate = Path.Combine("GVFS", "GVFS");
            string fileToReadThenDehydrate = Path.Combine(folderToDehydrate, "Program.cs");
            string fileToWriteThenDehydrate = Path.Combine(folderToDehydrate, "App.config");
            string fileToReadThenDehydrateVirtualPath = this.Enlistment.GetVirtualPathTo(fileToReadThenDehydrate);
            string fileToReadThenDehydrateBackingPath = this.Enlistment.GetBackingPathTo(fileToReadThenDehydrate);
            string fileToWriteThenDehydrateVirtualPath = this.Enlistment.GetVirtualPathTo(fileToWriteThenDehydrate);
            string fileToWriteThenDehydrateBackingPath = this.Enlistment.GetBackingPathTo(fileToWriteThenDehydrate);
            this.fileSystem.ReadAllText(fileToReadThenDehydrateVirtualPath);
            this.fileSystem.AppendAllText(fileToWriteThenDehydrateVirtualPath, "Append content");

            string folderToNotDehydrate = Path.Combine("GVFS", "GVFS.Common");
            string fileToReadThenNotDehydrate = Path.Combine(folderToNotDehydrate, "GVFSLock.cs");
            string fileToWriteThenNotDehydrate = Path.Combine(folderToNotDehydrate, "Enlistment.cs");
            string fileToReadThenNotDehydrateVirtualPath = this.Enlistment.GetVirtualPathTo(fileToReadThenNotDehydrate);
            string fileToReadThenNotDehydrateBackingPath = this.Enlistment.GetBackingPathTo(fileToReadThenNotDehydrate);
            string fileToWriteThenNotDehydrateVirtualPath = this.Enlistment.GetVirtualPathTo(fileToWriteThenNotDehydrate);
            string fileToWriteThenNotDehydrateBackingPath = this.Enlistment.GetBackingPathTo(fileToWriteThenNotDehydrate);
            this.fileSystem.ReadAllText(fileToReadThenNotDehydrateVirtualPath);
            this.fileSystem.AppendAllText(fileToWriteThenNotDehydrateVirtualPath, "Append content");
            GitProcess.Invoke(this.Enlistment.RepoRoot, $"reset --hard");

            this.DehydrateShouldSucceed(new[] { $"{folderToDehydrate} {FolderDehydrateSuccessfulMessage}" }, confirm: true, noStatus: false, foldersToDehydrate: folderToDehydrate);

            this.PlaceholdersShouldNotContain(folderToDehydrate, fileToReadThenDehydrate);
            GVFSHelpers.ModifiedPathsShouldNotContain(this.Enlistment, this.fileSystem, fileToWriteThenDehydrate.Replace(Path.DirectorySeparatorChar, TestConstants.GitPathSeparator));

            this.PlaceholdersShouldContain(folderToNotDehydrate, fileToReadThenNotDehydrate);
            GVFSHelpers.ModifiedPathsShouldContain(this.Enlistment, this.fileSystem, fileToWriteThenNotDehydrate.Replace(Path.DirectorySeparatorChar, TestConstants.GitPathSeparator));

            this.Enlistment.UnmountGVFS();

            fileToReadThenDehydrateBackingPath.ShouldNotExistOnDisk(this.fileSystem);
            fileToWriteThenDehydrateBackingPath.ShouldNotExistOnDisk(this.fileSystem);
            fileToReadThenNotDehydrateBackingPath.ShouldBeAFile(this.fileSystem);
            fileToWriteThenNotDehydrateBackingPath.ShouldBeAFile(this.fileSystem);
        }

        [TestCase]
        public void FolderDehydrateNestedFoldersChildBeforeParent()
        {
            string childFolderToDehydrate = Path.Combine("GVFS", "GVFS.Mount");
            string parentFolderToDehydrate = "GVFS";
            string fileToReadInChildFolder = Path.Combine(childFolderToDehydrate, "Program.cs");
            string fileToReadInOtherChildFolder = Path.Combine(parentFolderToDehydrate, "GVFS.UnitTests", "Program.cs");
            string fileToReadInChildFolderVirtualPath = this.Enlistment.GetVirtualPathTo(fileToReadInChildFolder);
            string fileToReadInChildFolderBackingPath = this.Enlistment.GetBackingPathTo(fileToReadInChildFolder);
            string fileToReadInOtherChildFolderVirtualPath = this.Enlistment.GetVirtualPathTo(fileToReadInOtherChildFolder);
            string fileToReadInOtherChildFolderBackingPath = this.Enlistment.GetBackingPathTo(fileToReadInOtherChildFolder);
            this.fileSystem.ReadAllText(fileToReadInChildFolderVirtualPath);
            this.fileSystem.ReadAllText(fileToReadInOtherChildFolderVirtualPath);

            this.DehydrateShouldSucceed(
                new[] { $"{childFolderToDehydrate} {FolderDehydrateSuccessfulMessage}", $"{parentFolderToDehydrate} {FolderDehydrateSuccessfulMessage}" },
                confirm: true,
                noStatus: false,
                foldersToDehydrate: string.Join(";", childFolderToDehydrate, parentFolderToDehydrate));

            this.Enlistment.UnmountGVFS();

            fileToReadInChildFolderBackingPath.ShouldNotExistOnDisk(this.fileSystem);
            fileToReadInOtherChildFolderBackingPath.ShouldNotExistOnDisk(this.fileSystem);
        }

        [TestCase]
        public void FolderDehydrateNestedFoldersParentBeforeChild()
        {
            string parentFolderToDehydrate = "GVFS";
            string childFolderToDehydrate = Path.Combine("GVFS", "GVFS.Mount");
            string fileToReadInOtherChildFolder = Path.Combine(parentFolderToDehydrate, "GVFS.UnitTests", "Program.cs");
            string fileToReadInChildFolder = Path.Combine(childFolderToDehydrate, "Program.cs");
            string fileToReadInOtherChildFolderVirtualPath = this.Enlistment.GetVirtualPathTo(fileToReadInOtherChildFolder);
            string fileToReadInOtherChildFolderBackingPath = this.Enlistment.GetBackingPathTo(fileToReadInOtherChildFolder);
            string fileToReadInChildFolderVirtualPath = this.Enlistment.GetVirtualPathTo(fileToReadInChildFolder);
            string fileToReadInChildFolderBackingPath = this.Enlistment.GetBackingPathTo(fileToReadInChildFolder);
            this.fileSystem.ReadAllText(fileToReadInOtherChildFolderVirtualPath);
            this.fileSystem.ReadAllText(fileToReadInChildFolderVirtualPath);

            this.DehydrateShouldSucceed(
                new[] { $"{parentFolderToDehydrate} {FolderDehydrateSuccessfulMessage}", $"Cannot dehydrate folder '{childFolderToDehydrate}': '{childFolderToDehydrate}' does not exist." },
                confirm: true,
                noStatus: false,
                foldersToDehydrate: string.Join(";", parentFolderToDehydrate, childFolderToDehydrate));

            this.Enlistment.UnmountGVFS();

            fileToReadInOtherChildFolderBackingPath.ShouldNotExistOnDisk(this.fileSystem);
            fileToReadInChildFolderBackingPath.ShouldNotExistOnDisk(this.fileSystem);
        }

        [TestCase]
        public void FolderDehydrateParentFolderInModifiedPathsShouldOutputMessage()
        {
            string folderToDelete = "GitCommandsTests";
            string folderToDeleteVirtualPath = this.Enlistment.GetVirtualPathTo(folderToDelete);
            this.fileSystem.DeleteDirectory(folderToDeleteVirtualPath);
            GitProcess.Invoke(this.Enlistment.RepoRoot, "reset --hard");

            string folderToDehydrate = Path.Combine(folderToDelete, "DeleteFileTests");
            this.Enlistment.GetVirtualPathTo(folderToDehydrate).ShouldBeADirectory(this.fileSystem);

            this.DehydrateShouldSucceed(new[] { $"Cannot dehydrate folder '{folderToDehydrate}': Must dehydrate parent folder '{folderToDelete}/'." }, confirm: true, noStatus: false, foldersToDehydrate: folderToDehydrate);
        }

        [TestCase]
        public void FolderDehydrateDirtyStatusShouldFail()
        {
            string folderToDehydrate = "GVFS";
            string fileToCreateVirtualPath = this.Enlistment.GetVirtualPathTo(folderToDehydrate, $"{nameof(this.FolderDehydrateDirtyStatusShouldFail)}.txt");
            this.fileSystem.WriteAllText(fileToCreateVirtualPath, "new file contents");
            fileToCreateVirtualPath.ShouldBeAFile(this.fileSystem);

            this.DehydrateShouldFail(new[] { "Running git status...Failed", "Untracked files:", "git status reported that you have dirty files" }, noStatus: false, foldersToDehydrate: folderToDehydrate);
            GitProcess.Invoke(this.Enlistment.RepoRoot, "clean -xdf");
        }

        [TestCase]
        public void FolderDehydrateDirtyStatusWithNoStatusShouldFail()
        {
            string folderToDehydrate = "GVFS";
            string fileToCreateVirtualPath = this.Enlistment.GetVirtualPathTo(folderToDehydrate, $"{nameof(this.FolderDehydrateDirtyStatusWithNoStatusShouldFail)}.txt");
            this.fileSystem.WriteAllText(fileToCreateVirtualPath, "new file contents");
            fileToCreateVirtualPath.ShouldBeAFile(this.fileSystem);

            this.DehydrateShouldFail(new[] { "Dehydrate --no-status not valid with --folders" }, noStatus: true, foldersToDehydrate: folderToDehydrate);
            GitProcess.Invoke(this.Enlistment.RepoRoot, "clean -xdf");
        }

        [TestCase]
        public void FolderDehydrateCannotDehydrateDotGitFolder()
        {
            this.DehydrateShouldSucceed(new[] { $"Cannot dehydrate folder '{TestConstants.DotGit.Root}': invalid folder path." }, confirm: true, noStatus: false, foldersToDehydrate: TestConstants.DotGit.Root);
            this.DehydrateShouldSucceed(new[] { $"Cannot dehydrate folder '{TestConstants.DotGit.Info.Root}': invalid folder path." }, confirm: true, noStatus: false, foldersToDehydrate: TestConstants.DotGit.Info.Root);
        }

        [TestCase]
        public void FolderDehydratePreviouslyDeletedFolder()
        {
            string folderToDehydrate = "TrailingSlashTests";
            string folderToDeleteVirtualPath = this.Enlistment.GetVirtualPathTo(folderToDehydrate);
            this.fileSystem.DeleteDirectory(folderToDeleteVirtualPath);
            GitProcess.Invoke(this.Enlistment.RepoRoot, "commit -a -m \"Delete a directory\"");

            GitProcess.Invoke(this.Enlistment.RepoRoot, "checkout -f HEAD~1");

            this.DehydrateShouldSucceed(new[] { $"{folderToDehydrate} {FolderDehydrateSuccessfulMessage}" }, confirm: true, noStatus: false, foldersToDehydrate: folderToDehydrate);

            folderToDeleteVirtualPath.ShouldBeADirectory(this.fileSystem);
        }

        [TestCase]
        public void FolderDehydrateTombstone()
        {
            string folderToDehydrate = "TrailingSlashTests";
            string folderToDeleteVirtualPath = this.Enlistment.GetVirtualPathTo(folderToDehydrate);
            this.fileSystem.DeleteDirectory(folderToDeleteVirtualPath);
            GitProcess.Invoke(this.Enlistment.RepoRoot, "commit -a -m \"Delete a directory\"");

            this.DehydrateShouldSucceed(new[] { $"{folderToDehydrate} {FolderDehydrateSuccessfulMessage}" }, confirm: true, noStatus: false, foldersToDehydrate: folderToDehydrate);

            folderToDeleteVirtualPath.ShouldNotExistOnDisk(this.fileSystem);
            GitProcess.Invoke(this.Enlistment.RepoRoot, "checkout HEAD~1");
            folderToDeleteVirtualPath.ShouldBeADirectory(this.fileSystem);
        }

        [TestCase]
        public void FolderDehydrateRelativePaths()
        {
            string[] foldersToDehydrate = new[]
            {
                Path.Combine("..", ".gvfs"),
                Path.DirectorySeparatorChar + Path.Combine("..", ".gvfs"),
                Path.Combine("GVFS", "..", "..", ".gvfs"),
                Path.Combine("GVFS/../../.gvfs"),
            };

            List<string> errorMessages = new List<string>();
            foreach (string path in foldersToDehydrate)
            {
                errorMessages.Add($"Cannot dehydrate folder '{path}': invalid folder path.");
            }

            this.DehydrateShouldSucceed(
                errorMessages.ToArray(),
                confirm: true,
                noStatus: false,
                foldersToDehydrate: foldersToDehydrate);
        }

        [TestCase]
        public void FolderDehydrateFolderThatDoesNotExist()
        {
            string folderToDehydrate = "DoesNotExist";
            this.DehydrateShouldSucceed(new[] { $"Cannot dehydrate folder '{folderToDehydrate}': '{folderToDehydrate}' does not exist." }, confirm: true, noStatus: false, foldersToDehydrate: folderToDehydrate);
        }

        [TestCase]
        public void FolderDehydrateNewlyCreatedFolderAndFile()
        {
            string folderToDehydrate = "NewFolder";
            string folderToCreateVirtualPath = this.Enlistment.GetVirtualPathTo(folderToDehydrate);
            string folderToCreateBackingPath = this.Enlistment.GetBackingPathTo(folderToDehydrate);
            this.fileSystem.CreateDirectory(folderToCreateVirtualPath);
            string fileToCreate = Path.Combine(folderToDehydrate, "newfile.txt");
            string fileToCreateVirtualPath = this.Enlistment.GetVirtualPathTo(fileToCreate);
            string fileToCreateBackingPath = this.Enlistment.GetBackingPathTo(fileToCreate);
            this.fileSystem.WriteAllText(fileToCreateVirtualPath, "Test content");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "add .");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "commit -m Test");

            this.DehydrateShouldSucceed(new[] { $"{folderToDehydrate} {FolderDehydrateSuccessfulMessage}" }, confirm: true, noStatus: false, foldersToDehydrate: folderToDehydrate);

            this.Enlistment.UnmountGVFS();
            fileToCreateBackingPath.ShouldNotExistOnDisk(this.fileSystem);
            this.CheckDehydratedFolderAfterUnmount(folderToCreateBackingPath);
        }

        private void PlaceholdersShouldContain(params string[] paths)
        {
            string[] placeholderLines = this.GetPlaceholderDatabaseLines();
            foreach (string path in paths)
            {
                placeholderLines.ShouldContain(x => x.StartsWith(path + GVFSHelpers.PlaceholderFieldDelimiter, FileSystemHelpers.PathComparison));
            }
        }

        private void PlaceholdersShouldNotContain(params string[] paths)
        {
            string[] placeholderLines = this.GetPlaceholderDatabaseLines();
            foreach (string path in paths)
            {
                placeholderLines.ShouldNotContain(x => x.StartsWith(path + Path.DirectorySeparatorChar, FileSystemHelpers.PathComparison) || x.Equals(path, FileSystemHelpers.PathComparison));
            }
        }

        private string[] GetPlaceholderDatabaseLines()
        {
            string placeholderDatabase = Path.Combine(this.Enlistment.DotGVFSRoot, "databases", "VFSForGit.sqlite");
            return GVFSHelpers.GetAllSQLitePlaceholdersAsString(placeholderDatabase).Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
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

        private void CheckDehydratedFolderAfterUnmount(string path)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                path.ShouldNotExistOnDisk(this.fileSystem);
            }
            else
            {
                path.ShouldBeADirectory(this.fileSystem);
            }
        }

        private void DehydrateShouldSucceed(string[] expectedInOutput, bool confirm, bool noStatus, params string[] foldersToDehydrate)
        {
            ProcessResult result = this.RunDehydrateProcess(confirm, noStatus, foldersToDehydrate);
            result.ExitCode.ShouldEqual(0, $"mount exit code was {result.ExitCode}. Output: {result.Output}");

            if (result.Output.Contains("Failed to move the src folder: Access to the path"))
            {
                string output = this.RunHandleProcess(Path.Combine(this.Enlistment.EnlistmentRoot, "src"));
                TestContext.Out.WriteLine(output);
            }

            result.Output.ShouldContain(expectedInOutput);
        }

        private void DehydrateShouldFail(string[] expectedErrorMessages, bool noStatus, params string[] foldersToDehydrate)
        {
            ProcessResult result = this.RunDehydrateProcess(confirm: true, noStatus: noStatus, foldersToDehydrate: foldersToDehydrate);
            result.ExitCode.ShouldEqual(GVFSGenericError, $"mount exit code was not {GVFSGenericError}");
            result.Output.ShouldContain(expectedErrorMessages);
        }

        private ProcessResult RunDehydrateProcess(bool confirm, bool noStatus, params string[] foldersToDehydrate)
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

            if (foldersToDehydrate.Length > 0)
            {
                dehydrateFlags += $" --folders {string.Join(";", foldersToDehydrate)}";
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
