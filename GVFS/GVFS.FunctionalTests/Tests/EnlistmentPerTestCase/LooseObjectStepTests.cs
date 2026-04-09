using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerTestCase
{
    [TestFixture]
    public class LooseObjectStepTests : TestsWithEnlistmentPerTestCase
    {
        private const string TempPackFolder = "tempPacks";
        private FileSystemRunner fileSystem;

        // Set forcePerRepoObjectCache to true to avoid any of the tests inadvertently corrupting
        // the cache
        public LooseObjectStepTests()
            : base(forcePerRepoObjectCache: true)
        {
            this.fileSystem = new SystemIORunner();
        }

        private string GitObjectRoot => this.Enlistment.GetObjectRoot(this.fileSystem);
        private string PackRoot => this.Enlistment.GetPackRoot(this.fileSystem);
        private string TempPackRoot => Path.Combine(this.PackRoot, TempPackFolder);

        [TestCase]
        [Category(Categories.NeedsReactionInCI)]
        public void RemoveLooseObjectsInPackFiles()
        {
            this.ClearAllObjects();

            // Copy and expand one pack
            this.ExpandOneTempPack(copyPackBackToPackDirectory: true);
            this.GetLooseObjectFiles().Count.ShouldBeAtLeast(1);
            this.CountPackFiles().ShouldEqual(1);

            // Cleanup should delete all loose objects, since they are in the packfile
            this.Enlistment.LooseObjectStep();

            this.GetLooseObjectFiles().Count.ShouldEqual(0);
            this.CountPackFiles().ShouldEqual(1);
            this.GetLooseObjectFiles().Count.ShouldEqual(0);
            this.CountPackFiles().ShouldEqual(1);
        }

        [TestCase]
        [Category(Categories.NeedsReactionInCI)]
        public void PutLooseObjectsInPackFiles()
        {
            this.ClearAllObjects();

            // Expand one pack, and verify we have loose objects
            this.ExpandOneTempPack(copyPackBackToPackDirectory: false);
            int looseObjectCount = this.GetLooseObjectFiles().Count();
            looseObjectCount.ShouldBeAtLeast(1);

            // This step should put the loose objects into a packfile
            this.Enlistment.LooseObjectStep();

            this.GetLooseObjectFiles().Count.ShouldEqual(looseObjectCount);
            this.CountPackFiles().ShouldEqual(1);

            // Running the step a second time should remove the loose obects and keep the pack file
            this.Enlistment.LooseObjectStep();

            this.GetLooseObjectFiles().Count.ShouldEqual(0);
            this.CountPackFiles().ShouldEqual(1);
        }

        [TestCase]
        public void NoLooseObjectsDoesNothing()
        {
            this.DeleteFiles(this.GetLooseObjectFiles());
            this.GetLooseObjectFiles().Count.ShouldEqual(0);
            int startingPackFileCount = this.CountPackFiles();

            this.Enlistment.LooseObjectStep();

            this.GetLooseObjectFiles().Count.ShouldEqual(0);
            this.CountPackFiles().ShouldEqual(startingPackFileCount);
        }

        [TestCase]
        [Category(Categories.NeedsReactionInCI)]
        public void CorruptLooseObjectIsDeleted()
        {
            this.ClearAllObjects();

            // Expand one pack, and verify we have loose objects
            this.ExpandOneTempPack(copyPackBackToPackDirectory: false);
            int looseObjectCount = this.GetLooseObjectFiles().Count();
            looseObjectCount.ShouldBeAtLeast(1, "Too few loose objects");

            // Create an invalid loose object
            string fakeBlobFolder = Path.Combine(this.GitObjectRoot, "00");
            string fakeBlob = Path.Combine(
                        fakeBlobFolder,
                        "01234567890123456789012345678901234567");
            this.fileSystem.CreateDirectory(fakeBlobFolder);
            this.fileSystem.CreateEmptyFile(fakeBlob);

            // This step should fail to place the objects, but
            // succeed in deleting the given file.
            this.Enlistment.LooseObjectStep();

            this.fileSystem.FileExists(fakeBlob).ShouldBeFalse(
                   "Step failed to delete corrupt blob");
            this.CountPackFiles().ShouldEqual(0, "Incorrect number of packs after first loose object step");
            this.GetLooseObjectFiles().Count.ShouldEqual(
                looseObjectCount,
                "unexpected number of loose objects after step");

            // This step should create a pack.
            this.Enlistment.LooseObjectStep();

            this.CountPackFiles().ShouldEqual(1, "Incorrect number of packs after second loose object step");
            this.GetLooseObjectFiles().Count.ShouldEqual(looseObjectCount);

            // This step should delete the loose objects
            this.Enlistment.LooseObjectStep();

            this.GetLooseObjectFiles().Count.ShouldEqual(0, "Incorrect number of loose objects after third loose object step");
        }

        private void ClearAllObjects()
        {
            this.Enlistment.UnmountGVFS();

            // Delete/Move any starting loose objects and packfiles
            this.DeleteFiles(this.GetLooseObjectFiles());
            this.MovePackFilesToTemp();
            this.GetLooseObjectFiles().Count.ShouldEqual(0, "incorrect number of loose objects after setup");
            this.CountPackFiles().ShouldEqual(0, "incorrect number of packs after setup");
        }

        private List<string> GetLooseObjectFiles()
        {
            List<string> looseObjectFiles = new List<string>();
            foreach (string directory in Directory.GetDirectories(this.GitObjectRoot))
            {
                // Check if the directory is 2 letter HEX
                if (Regex.IsMatch(directory, @"[/\\][0-9a-fA-F]{2}$"))
                {
                    string[] files = Directory.GetFiles(directory);
                    looseObjectFiles.AddRange(files);
                }
            }

            return looseObjectFiles;
        }

        private void DeleteFiles(List<string> filePaths)
        {
            foreach (string filePath in filePaths)
            {
                File.Delete(filePath);
            }
        }

        private int CountPackFiles()
        {
            return Directory.GetFiles(this.PackRoot, "*.pack").Length;
        }

        private void MovePackFilesToTemp()
        {
            string[] files = Directory.GetFiles(this.PackRoot);
            foreach (string file in files)
            {
                string path2 = Path.Combine(this.TempPackRoot, Path.GetFileName(file));

                File.Move(file, path2);
            }
        }

        private void ExpandOneTempPack(bool copyPackBackToPackDirectory)
        {
            // Find all pack files
            string[] packFiles = Directory.GetFiles(this.TempPackRoot, "pack-*.pack");
            Assert.Greater(packFiles.Length, 0);

            // Pick the first one found
            string packFile = packFiles[0];

            // Send the contents of the packfile to unpack-objects to example the loose objects
            // Note this won't work if the object exists in a pack file which is why we had to move them
            using (FileStream packFileStream = File.OpenRead(packFile))
            {
                string output = GitProcess.InvokeProcess(
                    this.Enlistment.RepoBackingRoot,
                    "unpack-objects",
                    new Dictionary<string, string>() { { "GIT_OBJECT_DIRECTORY", this.GitObjectRoot } },
                    inputStream: packFileStream).Output;
            }

            if (copyPackBackToPackDirectory)
            {
                // Copy the pack file back to packs
                string packFileName = Path.GetFileName(packFile);
                File.Copy(packFile, Path.Combine(this.PackRoot, packFileName));

                // Replace the '.pack' with '.idx' to copy the index file
                string packFileIndexName = packFileName.Replace(".pack", ".idx");
                File.Copy(Path.Combine(this.TempPackRoot, packFileIndexName), Path.Combine(this.PackRoot, packFileIndexName));
            }
        }
    }
}
