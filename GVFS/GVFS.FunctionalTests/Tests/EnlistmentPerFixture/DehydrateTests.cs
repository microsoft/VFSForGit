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
            string pathToEnumerate = this.Enlistment.GetVirtualPathTo("GVFS");
            this.fileSystem.EnumerateDirectory(pathToEnumerate);
            string subFolderToEnumerate = Path.Combine(pathToEnumerate, "GVFS");
            this.fileSystem.EnumerateDirectory(subFolderToEnumerate);

            this.DehydrateShouldSucceed(new[] { $"GVFS {FolderDehydrateSuccessfulMessage}" }, confirm: true, noStatus: false, foldersToDehydrate: "GVFS");
            this.Enlistment.UnmountGVFS();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                pathToEnumerate.ShouldNotExistOnDisk(this.fileSystem);
            }
            else
            {
                pathToEnumerate.ShouldBeADirectory(this.fileSystem);
            }

            subFolderToEnumerate.ShouldNotExistOnDisk(this.fileSystem);
        }

        [TestCase]
        public void FolderDehydrateFolderWithFilesThatWerePlaceholders()
        {
            string pathToReadFiles = this.Enlistment.GetVirtualPathTo("GVFS");
            string fileToRead = Path.Combine(pathToReadFiles, "GVFS", "Program.cs");
            using (File.OpenRead(fileToRead))
            {
            }

            this.DehydrateShouldSucceed(new[] { $"GVFS {FolderDehydrateSuccessfulMessage}" }, confirm: true, noStatus: false, foldersToDehydrate: "GVFS");
            this.Enlistment.UnmountGVFS();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                pathToReadFiles.ShouldNotExistOnDisk(this.fileSystem);
            }
            else
            {
                pathToReadFiles.ShouldBeADirectory(this.fileSystem);
            }

            fileToRead.ShouldNotExistOnDisk(this.fileSystem);
        }

        [TestCase]
        public void FolderDehydrateFolderWithFilesThatWereRead()
        {
            string pathToReadFiles = this.Enlistment.GetVirtualPathTo("GVFS");
            string fileToRead = Path.Combine(pathToReadFiles, "GVFS", "Program.cs");
            this.fileSystem.ReadAllText(fileToRead);

            this.fileSystem.EnumerateDirectory(pathToReadFiles);

            this.DehydrateShouldSucceed(new[] { $"GVFS {FolderDehydrateSuccessfulMessage}" }, confirm: true, noStatus: false, foldersToDehydrate: "GVFS");
            this.Enlistment.UnmountGVFS();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                pathToReadFiles.ShouldNotExistOnDisk(this.fileSystem);
            }
            else
            {
                pathToReadFiles.ShouldBeADirectory(this.fileSystem);
            }

            fileToRead.ShouldNotExistOnDisk(this.fileSystem);
        }

        [TestCase]
        public void FolderDehydrateFolderWithFilesThatWereWrittenTo()
        {
            string pathToWriteFiles = this.Enlistment.GetVirtualPathTo("GVFS");
            string fileToWriteTo = Path.Combine(pathToWriteFiles, "GVFS", "Program.cs");
            this.fileSystem.AppendAllText(fileToWriteTo, "Append content");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "add .");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "commit -m Test");

            this.DehydrateShouldSucceed(new[] { $"GVFS {FolderDehydrateSuccessfulMessage}" }, confirm: true, noStatus: false, foldersToDehydrate: "GVFS");
            this.Enlistment.UnmountGVFS();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                pathToWriteFiles.ShouldNotExistOnDisk(this.fileSystem);
            }
            else
            {
                pathToWriteFiles.ShouldBeADirectory(this.fileSystem);
            }

            fileToWriteTo.ShouldNotExistOnDisk(this.fileSystem);
        }

        [TestCase]
        public void FolderDehydrateFolderThatWasDeleted()
        {
            string pathToDelete = this.Enlistment.GetVirtualPathTo("Scripts");
            this.fileSystem.DeleteDirectory(pathToDelete);
            GitProcess.Invoke(this.Enlistment.RepoRoot, "checkout -- Scripts");

            this.DehydrateShouldSucceed(new[] { $"Scripts {FolderDehydrateSuccessfulMessage}" }, confirm: true, noStatus: false, foldersToDehydrate: "Scripts");
            this.Enlistment.UnmountGVFS();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                pathToDelete.ShouldNotExistOnDisk(this.fileSystem);
            }
            else
            {
                pathToDelete.ShouldBeADirectory(this.fileSystem);
                Path.Combine(pathToDelete, "RunUnitTests.bat").ShouldNotExistOnDisk(this.fileSystem);
            }
        }

        [TestCase]
        public void FolderDehydrateFolderThatIsSubstringOfExistingFolder()
        {
            string folderToDehydrate = Path.Combine("GVFS", "GVFS");
            string fileToRead = this.Enlistment.GetVirtualPathTo(Path.Combine(folderToDehydrate, "Program.cs"));
            string fileToWrite = this.Enlistment.GetVirtualPathTo(Path.Combine(folderToDehydrate, "App.config"));
            this.fileSystem.ReadAllText(fileToRead);
            this.fileSystem.AppendAllText(this.Enlistment.GetVirtualPathTo(fileToWrite), "Append content");

            string folderNotDehydrated = Path.Combine("GVFS", "GVFS.Common");
            string fileNotDehydrated = this.Enlistment.GetVirtualPathTo(Path.Combine(folderNotDehydrated, "GVFSLock.cs"));
            string fileNotDehydrated2 = this.Enlistment.GetVirtualPathTo(Path.Combine(folderNotDehydrated, "Enlistment.cs"));
            this.fileSystem.ReadAllText(fileNotDehydrated);
            this.fileSystem.AppendAllText(fileNotDehydrated2, "Append content");
            GitProcess.Invoke(this.Enlistment.RepoRoot, $"reset --hard");

            this.DehydrateShouldSucceed(new[] { $"{folderToDehydrate} {FolderDehydrateSuccessfulMessage}" }, confirm: true, noStatus: false, foldersToDehydrate: folderToDehydrate);

            this.PlaceholdersShouldNotContain(folderToDehydrate, Path.Combine(folderToDehydrate, "Program.cs"));
            GVFSHelpers.ModifiedPathsShouldNotContain(this.Enlistment, this.fileSystem, Path.Combine(folderToDehydrate, "App.config").Replace(Path.DirectorySeparatorChar, TestConstants.GitPathSeparator));

            this.PlaceholdersShouldContain(folderNotDehydrated, Path.Combine(folderNotDehydrated, "GVFSLock.cs"));
            GVFSHelpers.ModifiedPathsShouldContain(this.Enlistment, this.fileSystem, Path.Combine(folderNotDehydrated, "Enlistment.cs").Replace(Path.DirectorySeparatorChar, TestConstants.GitPathSeparator));

            this.Enlistment.UnmountGVFS();

            fileToRead.ShouldNotExistOnDisk(this.fileSystem);
            fileToWrite.ShouldNotExistOnDisk(this.fileSystem);
            fileNotDehydrated.ShouldBeAFile(this.fileSystem);
            fileNotDehydrated2.ShouldBeAFile(this.fileSystem);
        }

        [TestCase]
        public void FolderDehydrateNestedFoldersChildBeforeParent()
        {
            string folderToDehydrate1 = Path.Combine("GVFS", "GVFS.Mount");
            string folderToDehydrate2 = "GVFS";
            string fileToRead1 = this.Enlistment.GetVirtualPathTo(Path.Combine(folderToDehydrate1, "Program.cs"));
            string fileToRead2 = this.Enlistment.GetVirtualPathTo(Path.Combine(folderToDehydrate2, "GVFS.UnitTests", "Program.cs"));
            this.fileSystem.ReadAllText(fileToRead1);
            this.fileSystem.ReadAllText(fileToRead2);

            this.DehydrateShouldSucceed(
                new[] { $"{folderToDehydrate1} {FolderDehydrateSuccessfulMessage}", $"{folderToDehydrate2} {FolderDehydrateSuccessfulMessage}" },
                confirm: true,
                noStatus: false,
                foldersToDehydrate: string.Join(";", folderToDehydrate1, folderToDehydrate2));

            this.Enlistment.UnmountGVFS();

            fileToRead1.ShouldNotExistOnDisk(this.fileSystem);
            fileToRead2.ShouldNotExistOnDisk(this.fileSystem);
        }

        [TestCase]
        public void FolderDehydrateNestedFoldersParentBeforeChild()
        {
            string folderToDehydrate1 = "GVFS";
            string folderToDehydrate2 = Path.Combine("GVFS", "GVFS.Mount");
            string fileToRead1 = this.Enlistment.GetVirtualPathTo(Path.Combine(folderToDehydrate1, "GVFS.UnitTests", "Program.cs"));
            string fileToRead2 = this.Enlistment.GetVirtualPathTo(Path.Combine(folderToDehydrate2, "Program.cs"));
            this.fileSystem.ReadAllText(fileToRead1);
            this.fileSystem.ReadAllText(fileToRead2);

            this.DehydrateShouldSucceed(
                new[] { $"{folderToDehydrate1} {FolderDehydrateSuccessfulMessage}", $"Cannot dehydrate folder '{folderToDehydrate2}': '{folderToDehydrate2}' does not exist." },
                confirm: true,
                noStatus: false,
                foldersToDehydrate: string.Join(";", folderToDehydrate1, folderToDehydrate2));

            this.Enlistment.UnmountGVFS();

            fileToRead1.ShouldNotExistOnDisk(this.fileSystem);
            fileToRead2.ShouldNotExistOnDisk(this.fileSystem);
        }

        [TestCase]
        public void FolderDehydrateParentFolderInModifiedPathsShouldOutputMessage()
        {
            string pathToDelete = this.Enlistment.GetVirtualPathTo("GitCommandsTests");
            this.fileSystem.DeleteDirectory(pathToDelete);
            GitProcess.Invoke(this.Enlistment.RepoRoot, "reset --hard");

            string folderToDehydrate = Path.Combine("GitCommandsTests", "DeleteFileTests");
            this.Enlistment.GetVirtualPathTo(folderToDehydrate).ShouldBeADirectory(this.fileSystem);

            this.DehydrateShouldSucceed(new[] { $"Cannot dehydrate folder '{folderToDehydrate}': Must dehydrate parent folder 'GitCommandsTests/'." }, confirm: true, noStatus: false, foldersToDehydrate: folderToDehydrate);
        }

        [TestCase]
        public void FolderDehydrateDirtyStatusShouldFail()
        {
            string fileToCreate = this.Enlistment.GetVirtualPathTo(Path.Combine("GVFS", $"{nameof(this.FolderDehydrateDirtyStatusShouldFail)}.txt"));
            this.fileSystem.WriteAllText(fileToCreate, "new file contents");
            fileToCreate.ShouldBeAFile(this.fileSystem);

            this.DehydrateShouldFail(new[] { "Running git status...Failed", "Untracked files:", "git status reported that you have dirty files" }, noStatus: false, foldersToDehydrate: "GVFS");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "clean -xdf");
        }

        [TestCase]
        public void FolderDehydrateDirtyStatusWithNoStatusShouldFail()
        {
            string fileToCreate = this.Enlistment.GetVirtualPathTo(Path.Combine("GVFS", $"{nameof(this.FolderDehydrateDirtyStatusWithNoStatusShouldFail)}.txt"));
            this.fileSystem.WriteAllText(fileToCreate, "new file contents");
            fileToCreate.ShouldBeAFile(this.fileSystem);

            this.DehydrateShouldFail(new[] { "Dehydrate --no-status not valid with --folders" }, noStatus: true, foldersToDehydrate: "GVFS");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "clean -xdf");
        }

        [TestCase]
        public void FolderDehydrateCannotDehydrateDotGitFolder()
        {
            this.DehydrateShouldSucceed(new[] { $"Cannot dehydrate folder '{TestConstants.DotGit.Root}': invalid folder path." }, confirm: true, noStatus: false, foldersToDehydrate: TestConstants.DotGit.Root);
            this.DehydrateShouldSucceed(new[] { $"Cannot dehydrate folder '{TestConstants.DotGit.Info.Root}': invalid folder path." }, confirm: true, noStatus: false, foldersToDehydrate: TestConstants.DotGit.Info.Root);
        }

        [TestCase]
        public void FolderDehydrateCreatedDirectoryParentFolderInModifiedPathsShouldOutputMessage()
        {
            string pathToDelete = this.Enlistment.GetVirtualPathTo("GitCommandsTests");
            this.fileSystem.DeleteDirectory(pathToDelete);
            GitProcess.Invoke(this.Enlistment.RepoRoot, "reset --hard");

            string folderToDehydrate = Path.Combine("GitCommandsTests", "DeleteFileTests");
            this.Enlistment.GetVirtualPathTo(folderToDehydrate).ShouldBeADirectory(this.fileSystem);

            this.DehydrateShouldSucceed(new[] { $"Cannot dehydrate folder '{folderToDehydrate}': Must dehydrate parent folder 'GitCommandsTests/'." }, confirm: true, noStatus: false, foldersToDehydrate: folderToDehydrate);
        }

        [TestCase]
        public void FolderDehydratePreviouslyDeletedFolder()
        {
            string pathToDelete = this.Enlistment.GetVirtualPathTo("TrailingSlashTests");
            this.fileSystem.DeleteDirectory(pathToDelete);
            GitProcess.Invoke(this.Enlistment.RepoRoot, "commit -a -m \"Delete a directory\"");

            GitProcess.Invoke(this.Enlistment.RepoRoot, "checkout -f HEAD~1");

            string folderToDehydrate = "TrailingSlashTests";
            this.DehydrateShouldSucceed(new[] { $"{folderToDehydrate} {FolderDehydrateSuccessfulMessage}" }, confirm: true, noStatus: false, foldersToDehydrate: folderToDehydrate);

            pathToDelete.ShouldBeADirectory(this.fileSystem);
        }

        [TestCase]
        public void FolderDehydrateTombstone()
        {
            string pathToDelete = this.Enlistment.GetVirtualPathTo("TrailingSlashTests");
            this.fileSystem.DeleteDirectory(pathToDelete);
            GitProcess.Invoke(this.Enlistment.RepoRoot, "commit -a -m \"Delete a directory\"");

            string folderToDehydrate = "TrailingSlashTests";
            this.DehydrateShouldSucceed(new[] { $"{folderToDehydrate} {FolderDehydrateSuccessfulMessage}" }, confirm: true, noStatus: false, foldersToDehydrate: folderToDehydrate);

            pathToDelete.ShouldNotExistOnDisk(this.fileSystem);
            GitProcess.Invoke(this.Enlistment.RepoRoot, "checkout HEAD~1");
            pathToDelete.ShouldBeADirectory(this.fileSystem);
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
            string directoryToCreate = this.Enlistment.GetVirtualPathTo("NewFolder");
            this.fileSystem.CreateDirectory(directoryToCreate);
            string fileToCreate = Path.Combine(directoryToCreate, "newfile.txt");
            this.fileSystem.WriteAllText(fileToCreate, "Test content");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "add .");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "commit -m Test");

            this.DehydrateShouldSucceed(new[] { $"NewFolder {FolderDehydrateSuccessfulMessage}" }, confirm: true, noStatus: false, foldersToDehydrate: "NewFolder");

            this.Enlistment.UnmountGVFS();
            fileToCreate.ShouldNotExistOnDisk(this.fileSystem);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                directoryToCreate.ShouldNotExistOnDisk(this.fileSystem);
            }
            else
            {
                directoryToCreate.ShouldBeADirectory(this.fileSystem);
            }
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
