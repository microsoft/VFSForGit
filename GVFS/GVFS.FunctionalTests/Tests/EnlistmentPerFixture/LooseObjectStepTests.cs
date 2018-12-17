using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Tools;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    public class LooseObjectStepTests : TestsWithEnlistmentPerFixture
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
        private string TempPackRoot=> Path.Combine(this.PackRoot, TempPackFolder);

        [TestCase]
        public void RemoveLooseObjectsInPackFiles()
        {
            // Delete any starting loose objects and verify
            this.DeleteFiles(this.GetLooseObjectFiles());
            Assert.AreEqual(0, this.GetLooseObjectFiles().Count);

            // Move packfiles to temp
            this.MovePackFilesToTemp();

            // Expand 1 pack file and Copy it back to packs
            this.ExpandOneTempPackAndMoveBack();

            // Verify we have some LooseObjects
            // These objects will also appear in the pack file that was moved back
            Assert.AreNotEqual(0, this.GetLooseObjectFiles().Count);

            // Run Cleanup
            this.Enlistment.LooseObjectStep();

            // Verify loose objects appearing in the pack file are removed
            Assert.AreEqual(0, this.GetLooseObjectFiles().Count);
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

        private void MovePackFilesToTemp()
        {
            string[] files = Directory.GetFiles(this.PackRoot);
            foreach (string file in files)
            {
                string path2 = Path.Combine(this.TempPackRoot, Path.GetFileName(file));
                File.Move(file, path2);
            }
        }

        private void ExpandOneTempPackAndMoveBack()
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
                    this.Enlistment.RepoRoot, 
                    "unpack-objects", 
                    new Dictionary<string, string>() { { "GIT_OBJECT_DIRECTORY", this.GitObjectRoot } }, 
                    inputStream: packFileStream).Output;
            }

            // Copy the pack file back to packs
            string packFileName = Path.GetFileName(packFile);
            File.Copy(packFile, Path.Combine(this.PackRoot, packFileName));

            // Replace the '.pack' with '.idx' to copy the index file
            string packFileIndexName = packFileName.Replace(".pack", ".idx");
            File.Copy(Path.Combine(this.TempPackRoot, packFileIndexName), Path.Combine(this.PackRoot, packFileIndexName));
        }
    }
}
