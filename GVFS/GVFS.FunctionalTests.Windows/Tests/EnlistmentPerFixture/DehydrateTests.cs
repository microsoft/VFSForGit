using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using Microsoft.Win32.SafeHandles;
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
        private const uint FileFlagBackupSemantics = 0x02000000;
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
            TestPath folderToEnumerate = new TestPath(this.Enlistment, folderToDehydrate);
            TestPath subFolderToEnumerate = new TestPath(this.Enlistment, Path.Combine(folderToDehydrate, "GVFS"));
            this.fileSystem.EnumerateDirectory(folderToEnumerate.VirtualPath);
            this.fileSystem.EnumerateDirectory(subFolderToEnumerate.VirtualPath);

            this.DehydrateShouldSucceed(new[] { $"{folderToDehydrate} {FolderDehydrateSuccessfulMessage}" }, confirm: true, noStatus: false, foldersToDehydrate: folderToDehydrate);
            this.Enlistment.UnmountGVFS();

            // Use the backing path because on some platforms
            // the virtual path is no longer accessible after unmounting.
            this.CheckDehydratedFolderAfterUnmount(folderToEnumerate.BackingPath);
            subFolderToEnumerate.BackingPath.ShouldNotExistOnDisk(this.fileSystem);
        }

        [TestCase]
        public void FolderDehydrateFolderWithFilesThatWerePlaceholders()
        {
            string folderToDehydrate = "GVFS";
            TestPath folderToReadFiles = new TestPath(this.Enlistment, folderToDehydrate);
            TestPath fileToRead = new TestPath(this.Enlistment, Path.Combine(folderToDehydrate, "GVFS", "Program.cs"));

            using (File.OpenRead(fileToRead.VirtualPath))
            {
            }

            this.DehydrateShouldSucceed(new[] { $"{folderToDehydrate} {FolderDehydrateSuccessfulMessage}" }, confirm: true, noStatus: false, foldersToDehydrate: folderToDehydrate);
            this.Enlistment.UnmountGVFS();

            // Use the backing path because on some platforms
            // the virtual path is no longer accessible after unmounting.
            this.CheckDehydratedFolderAfterUnmount(folderToReadFiles.BackingPath);
            fileToRead.BackingPath.ShouldNotExistOnDisk(this.fileSystem);
        }

        [TestCase]
        public void FolderDehydrateFolderWithFilesThatWereRead()
        {
            string folderToDehydrate = "GVFS";
            TestPath folderToReadFiles = new TestPath(this.Enlistment, folderToDehydrate);
            TestPath fileToRead = new TestPath(this.Enlistment, Path.Combine(folderToDehydrate, "GVFS", "Program.cs"));
            this.fileSystem.ReadAllText(fileToRead.VirtualPath);
            this.fileSystem.EnumerateDirectory(folderToReadFiles.VirtualPath);

            this.DehydrateShouldSucceed(new[] { $"{folderToDehydrate} {FolderDehydrateSuccessfulMessage}" }, confirm: true, noStatus: false, foldersToDehydrate: folderToDehydrate);
            this.Enlistment.UnmountGVFS();

            // Use the backing path because on some platforms
            // the virtual path is no longer accessible after unmounting.
            this.CheckDehydratedFolderAfterUnmount(folderToReadFiles.BackingPath);
            fileToRead.BackingPath.ShouldNotExistOnDisk(this.fileSystem);
        }

        [TestCase]
        public void FolderDehydrateFolderWithFilesThatWereWrittenTo()
        {
            string folderToDehydrate = "GVFS";
            TestPath folderToWriteFiles = new TestPath(this.Enlistment, folderToDehydrate);
            TestPath fileToWrite = new TestPath(this.Enlistment, Path.Combine(folderToDehydrate, "GVFS", "Program.cs"));
            this.fileSystem.AppendAllText(fileToWrite.VirtualPath, "Append content");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "add .");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "commit -m Test");

            this.DehydrateShouldSucceed(new[] { $"{folderToDehydrate} {FolderDehydrateSuccessfulMessage}" }, confirm: true, noStatus: false, foldersToDehydrate: folderToDehydrate);
            this.Enlistment.UnmountGVFS();

            // Use the backing path because on some platforms
            // the virtual path is no longer accessible after unmounting.
            this.CheckDehydratedFolderAfterUnmount(folderToWriteFiles.BackingPath);
            fileToWrite.BackingPath.ShouldNotExistOnDisk(this.fileSystem);
        }

        [TestCase]
        public void FolderDehydrateFolderThatWasDeleted()
        {
            string folderToDehydrate = "Scripts";
            TestPath folderToDelete = new TestPath(this.Enlistment, folderToDehydrate);
            this.fileSystem.DeleteDirectory(folderToDelete.VirtualPath);
            GitProcess.Invoke(this.Enlistment.RepoRoot, $"checkout -- {folderToDehydrate}");

            this.DehydrateShouldSucceed(new[] { $"{folderToDehydrate} {FolderDehydrateSuccessfulMessage}" }, confirm: true, noStatus: false, foldersToDehydrate: folderToDehydrate);
            this.Enlistment.UnmountGVFS();

            // Use the backing path because on some platforms
            // the virtual path is no longer accessible after unmounting.
            this.CheckDehydratedFolderAfterUnmount(folderToDelete.BackingPath);
            Path.Combine(folderToDelete.BackingPath, "RunUnitTests.bat").ShouldNotExistOnDisk(this.fileSystem);
        }

        [TestCase]
        [Category(Categories.WindowsOnly)]
        public void FolderDehydrateFolderThatIsLocked()
        {
            const string folderToDehydrate = "GVFS";
            const string folderToLock = "GVFS.Service";
            TestPath folderPathDehydrated = new TestPath(this.Enlistment, folderToDehydrate);
            TestPath folderPathToLock = new TestPath(this.Enlistment, Path.Combine(folderToDehydrate, folderToLock));
            TestPath fileToWrite = new TestPath(this.Enlistment, Path.Combine(folderToDehydrate, folderToLock, "Program.cs"));
            this.fileSystem.AppendAllText(fileToWrite.VirtualPath, "Append content");
            GitProcess.Invoke(this.Enlistment.RepoRoot, $"reset --hard");
            using (SafeFileHandle handle = this.OpenFolderHandle(folderPathToLock.VirtualPath))
            {
                handle.IsInvalid.ShouldEqual(false);
                this.DehydrateShouldSucceed(new[] { $"{folderToDehydrate} {FolderDehydrateSuccessfulMessage}" }, confirm: true, noStatus: false, foldersToDehydrate: folderToDehydrate);
            }

            this.Enlistment.UnmountGVFS();

            folderPathToLock.BackingPath.ShouldBeADirectory(this.fileSystem).WithNoItems();
            folderPathDehydrated.BackingPath.ShouldBeADirectory(this.fileSystem).WithOneItem().Name.ShouldEqual(folderToLock);
        }

        [TestCase]
        public void FolderDehydrateFolderThatIsSubstringOfExistingFolder()
        {
            string folderToDehydrate = Path.Combine("GVFS", "GVFS");
            TestPath fileToReadThenDehydrate = new TestPath(this.Enlistment, Path.Combine(folderToDehydrate, "Program.cs"));
            TestPath fileToWriteThenDehydrate = new TestPath(this.Enlistment, Path.Combine(folderToDehydrate, "App.config"));
            this.fileSystem.ReadAllText(fileToReadThenDehydrate.VirtualPath);
            this.fileSystem.AppendAllText(fileToWriteThenDehydrate.VirtualPath, "Append content");

            string folderToNotDehydrate = Path.Combine("GVFS", "GVFS.Common");
            TestPath fileToReadThenNotDehydrate = new TestPath(this.Enlistment, Path.Combine(folderToNotDehydrate, "GVFSLock.cs"));
            TestPath fileToWriteThenNotDehydrate = new TestPath(this.Enlistment, Path.Combine(folderToNotDehydrate, "Enlistment.cs"));
            this.fileSystem.ReadAllText(fileToReadThenNotDehydrate.VirtualPath);
            this.fileSystem.AppendAllText(fileToWriteThenNotDehydrate.VirtualPath, "Append content");
            GitProcess.Invoke(this.Enlistment.RepoRoot, $"reset --hard");

            this.DehydrateShouldSucceed(new[] { $"{folderToDehydrate} {FolderDehydrateSuccessfulMessage}" }, confirm: true, noStatus: false, foldersToDehydrate: folderToDehydrate);

            this.PlaceholdersShouldNotContain(folderToDehydrate, fileToReadThenDehydrate.BasePath);
            GVFSHelpers.ModifiedPathsShouldNotContain(this.Enlistment, this.fileSystem, fileToWriteThenDehydrate.BasePath.Replace(Path.DirectorySeparatorChar, TestConstants.GitPathSeparator));

            this.PlaceholdersShouldContain(folderToNotDehydrate, fileToReadThenNotDehydrate.BasePath);
            GVFSHelpers.ModifiedPathsShouldContain(this.Enlistment, this.fileSystem, fileToWriteThenNotDehydrate.BasePath.Replace(Path.DirectorySeparatorChar, TestConstants.GitPathSeparator));

            this.Enlistment.UnmountGVFS();

            // Use the backing path because on some platforms
            // the virtual path is no longer accessible after unmounting.
            fileToReadThenDehydrate.BackingPath.ShouldNotExistOnDisk(this.fileSystem);
            fileToWriteThenDehydrate.BackingPath.ShouldNotExistOnDisk(this.fileSystem);
            fileToReadThenNotDehydrate.BackingPath.ShouldBeAFile(this.fileSystem);
            fileToWriteThenNotDehydrate.BackingPath.ShouldBeAFile(this.fileSystem);
        }

        [TestCase]
        public void FolderDehydrateNestedFoldersChildBeforeParent()
        {
            string parentFolderToDehydrate = "GVFS";
            string childFolderToDehydrate = Path.Combine(parentFolderToDehydrate, "GVFS.Mount");
            TestPath fileToReadInChildFolder = new TestPath(this.Enlistment, Path.Combine(childFolderToDehydrate, "Program.cs"));
            TestPath fileToReadInOtherChildFolder = new TestPath(this.Enlistment, Path.Combine(parentFolderToDehydrate, "GVFS.UnitTests", "Program.cs"));
            this.fileSystem.ReadAllText(fileToReadInChildFolder.VirtualPath);
            this.fileSystem.ReadAllText(fileToReadInOtherChildFolder.VirtualPath);

            this.DehydrateShouldSucceed(
                new[] { $"{childFolderToDehydrate} {FolderDehydrateSuccessfulMessage}", $"{parentFolderToDehydrate} {FolderDehydrateSuccessfulMessage}" },
                confirm: true,
                noStatus: false,
                foldersToDehydrate: string.Join(";", childFolderToDehydrate, parentFolderToDehydrate));

            this.Enlistment.UnmountGVFS();

            // Use the backing path because on some platforms
            // the virtual path is no longer accessible after unmounting.
            fileToReadInChildFolder.BackingPath.ShouldNotExistOnDisk(this.fileSystem);
            fileToReadInOtherChildFolder.BackingPath.ShouldNotExistOnDisk(this.fileSystem);
        }

        [TestCase]
        public void FolderDehydrateNestedFoldersParentBeforeChild()
        {
            string parentFolderToDehydrate = "GVFS";
            string childFolderToDehydrate = Path.Combine(parentFolderToDehydrate, "GVFS.Mount");
            TestPath fileToReadInChildFolder = new TestPath(this.Enlistment, Path.Combine(childFolderToDehydrate, "Program.cs"));
            TestPath fileToReadInOtherChildFolder = new TestPath(this.Enlistment, Path.Combine(parentFolderToDehydrate, "GVFS.UnitTests", "Program.cs"));
            this.fileSystem.ReadAllText(fileToReadInChildFolder.VirtualPath);
            this.fileSystem.ReadAllText(fileToReadInOtherChildFolder.VirtualPath);

            this.DehydrateShouldSucceed(
                new[] { $"{parentFolderToDehydrate} {FolderDehydrateSuccessfulMessage}", $"Cannot dehydrate folder '{childFolderToDehydrate}': '{childFolderToDehydrate}' does not exist." },
                confirm: true,
                noStatus: false,
                foldersToDehydrate: string.Join(";", parentFolderToDehydrate, childFolderToDehydrate));

            this.Enlistment.UnmountGVFS();

            // Use the backing path because on some platforms
            // the virtual path is no longer accessible after unmounting.
            fileToReadInChildFolder.BackingPath.ShouldNotExistOnDisk(this.fileSystem);
            fileToReadInOtherChildFolder.BackingPath.ShouldNotExistOnDisk(this.fileSystem);
        }

        [TestCase]
        public void FolderDehydrateParentFolderInModifiedPathsShouldOutputMessage()
        {
            string folderToDehydrateParentFolder = "GitCommandsTests";
            TestPath folderToDelete = new TestPath(this.Enlistment, folderToDehydrateParentFolder);
            this.fileSystem.DeleteDirectory(folderToDelete.VirtualPath);
            GitProcess.Invoke(this.Enlistment.RepoRoot, "reset --hard");

            string folderToDehydrate = Path.Combine(folderToDehydrateParentFolder, "DeleteFileTests");
            this.Enlistment.GetVirtualPathTo(folderToDehydrate).ShouldBeADirectory(this.fileSystem);

            this.DehydrateShouldSucceed(new[] { $"Cannot dehydrate folder '{folderToDehydrate}': Must dehydrate parent folder '{folderToDehydrateParentFolder}/'." }, confirm: true, noStatus: false, foldersToDehydrate: folderToDehydrate);
        }

        [TestCase]
        public void FolderDehydrateDirtyStatusShouldFail()
        {
            string folderToDehydrate = "GVFS";
            TestPath fileToCreate = new TestPath(this.Enlistment, Path.Combine(folderToDehydrate, $"{nameof(this.FolderDehydrateDirtyStatusShouldFail)}.txt"));
            this.fileSystem.WriteAllText(fileToCreate.VirtualPath, "new file contents");
            fileToCreate.VirtualPath.ShouldBeAFile(this.fileSystem);

            this.DehydrateShouldFail(new[] { "Running git status...Failed", "Untracked files:", "git status reported that you have dirty files" }, noStatus: false, foldersToDehydrate: folderToDehydrate);
            GitProcess.Invoke(this.Enlistment.RepoRoot, "clean -xdf");
        }

        [TestCase]
        public void FolderDehydrateDirtyStatusWithNoStatusShouldFail()
        {
            string folderToDehydrate = "GVFS";
            TestPath fileToCreate = new TestPath(this.Enlistment, Path.Combine(folderToDehydrate, $"{nameof(this.FolderDehydrateDirtyStatusWithNoStatusShouldFail)}.txt"));
            this.fileSystem.WriteAllText(fileToCreate.VirtualPath, "new file contents");
            fileToCreate.VirtualPath.ShouldBeAFile(this.fileSystem);

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
        public void FolderDehydratePreviouslyDeletedFolders()
        {
            string folderToDehydrate = "TrailingSlashTests";
            TestPath folderToDelete = new TestPath(this.Enlistment, folderToDehydrate);
            string secondFolderToDehydrate = "FilenameEncoding";
            TestPath secondFolderToDelete = new TestPath(this.Enlistment, secondFolderToDehydrate);

            this.fileSystem.DeleteDirectory(folderToDelete.VirtualPath);
            this.fileSystem.DeleteDirectory(secondFolderToDelete.VirtualPath);
            GitProcess.Invoke(this.Enlistment.RepoRoot, "commit -a -m \"Delete directories\"");

            folderToDelete.VirtualPath.ShouldNotExistOnDisk(this.fileSystem);
            secondFolderToDelete.VirtualPath.ShouldNotExistOnDisk(this.fileSystem);

            GitProcess.Invoke(this.Enlistment.RepoRoot, "checkout -f HEAD~1");

            folderToDelete.VirtualPath.ShouldBeADirectory(this.fileSystem);
            secondFolderToDelete.VirtualPath.ShouldBeADirectory(this.fileSystem);

            this.DehydrateShouldSucceed(
                new[]
                {
                    $"{folderToDehydrate} {FolderDehydrateSuccessfulMessage}",
                    $"{secondFolderToDehydrate} {FolderDehydrateSuccessfulMessage}",
                },
                confirm: true,
                noStatus: false,
                foldersToDehydrate: new[] { folderToDehydrate, secondFolderToDehydrate });

            folderToDelete.VirtualPath.ShouldBeADirectory(this.fileSystem);
            secondFolderToDelete.VirtualPath.ShouldBeADirectory(this.fileSystem);
        }

        [TestCase]
        public void FolderDehydrateTombstone()
        {
            string folderToDehydrate = "TrailingSlashTests";
            TestPath folderToDelete = new TestPath(this.Enlistment, folderToDehydrate);
            this.fileSystem.DeleteDirectory(folderToDelete.VirtualPath);
            GitProcess.Invoke(this.Enlistment.RepoRoot, "commit -a -m \"Delete a directory\"");

            this.DehydrateShouldSucceed(new[] { $"{folderToDehydrate} {FolderDehydrateSuccessfulMessage}" }, confirm: true, noStatus: false, foldersToDehydrate: folderToDehydrate);

            folderToDelete.VirtualPath.ShouldNotExistOnDisk(this.fileSystem);
            GitProcess.Invoke(this.Enlistment.RepoRoot, "checkout HEAD~1");
            folderToDelete.VirtualPath.ShouldBeADirectory(this.fileSystem);
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
            TestPath folderToCreate = new TestPath(this.Enlistment, folderToDehydrate);
            this.fileSystem.CreateDirectory(folderToCreate.VirtualPath);
            TestPath fileToCreate = new TestPath(this.Enlistment, Path.Combine(folderToDehydrate, "newfile.txt"));
            this.fileSystem.WriteAllText(fileToCreate.VirtualPath, "Test content");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "add .");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "commit -m Test");

            this.DehydrateShouldSucceed(new[] { $"{folderToDehydrate} {FolderDehydrateSuccessfulMessage}" }, confirm: true, noStatus: false, foldersToDehydrate: folderToDehydrate);

            this.Enlistment.UnmountGVFS();

            // Use the backing path because on some platforms
            // the virtual path is no longer accessible after unmounting.
            fileToCreate.BackingPath.ShouldNotExistOnDisk(this.fileSystem);
            this.CheckDehydratedFolderAfterUnmount(folderToCreate.BackingPath);
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

        private SafeFileHandle OpenFolderHandle(string path)
        {
            return NativeMethods.CreateFile(
                path,
                (uint)FileAccess.Read,
                FileShare.Read,
                IntPtr.Zero,
                FileMode.Open,
                FileFlagBackupSemantics,
                IntPtr.Zero);
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

        private class TestPath
        {
            public TestPath(GVFSFunctionalTestEnlistment enlistment, string basePath)
            {
                this.BasePath = basePath;
                this.VirtualPath = enlistment.GetVirtualPathTo(basePath);
                this.BackingPath = enlistment.GetBackingPathTo(basePath);
            }

            public string BasePath { get; }
            public string VirtualPath { get; }
            public string BackingPath { get; }
        }
    }
}
