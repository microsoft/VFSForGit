using RGFS.Tests.Should;
using NUnit.Framework;
using System.IO;

namespace RGFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    public class PrefetchVerbTests : TestsWithEnlistmentPerFixture
    {
        [TestCase]
        public void PrefetchAll()
        {
            this.ExpectBlobCount(this.Enlistment.Prefetch("--files *"), 491);
            this.ExpectBlobCount(this.Enlistment.Prefetch("--folders /"), 491);
            this.ExpectBlobCount(this.Enlistment.Prefetch("--folders \\"), 491);
        }

        [TestCase]
        public void PrefetchAllMustBeExplicit()
        {
            this.Enlistment.Prefetch(string.Empty).ShouldContain("Did you mean to fetch all blobs?");
        }

        [TestCase]
        public void PrefetchSpecificFiles()
        {
            this.ExpectBlobCount(this.Enlistment.Prefetch(@"--files RGFS\RGFS\Program.cs"), 1);
            this.ExpectBlobCount(this.Enlistment.Prefetch(@"--files RGFS\RGFS\Program.cs;RGFS\RGFS.FunctionalTests\RGFS.FunctionalTests.csproj"), 2);
        }

        [TestCase]
        public void PrefetchByFileExtension()
        {
            this.ExpectBlobCount(this.Enlistment.Prefetch("--files *.cs"), 199);
            this.ExpectBlobCount(this.Enlistment.Prefetch("--files *.cs;*.csproj"), 208);
        }

        [TestCase]
        public void PrefetchFolders()
        {
            this.ExpectBlobCount(this.Enlistment.Prefetch(@"--folders RGFS\RGFS"), 17);
            this.ExpectBlobCount(this.Enlistment.Prefetch(@"--folders RGFS\RGFS;RGFS\RGFS.FunctionalTests"), 65);
        }

        [TestCase]
        public void PrefetchIsAllowedToDoNothing()
        {
            this.ExpectBlobCount(this.Enlistment.Prefetch("--files nonexistent.txt"), 0);
            this.ExpectBlobCount(this.Enlistment.Prefetch("--folders nonexistent_folder"), 0);
        }

        [TestCase]
        public void PrefetchFolderListFromFile()
        {
            string tempFilePath = Path.Combine(Path.GetTempPath(), "temp.file");
            File.WriteAllLines(
                tempFilePath,
                new[]
                {
                    "# A comment",
                    " ",
                    "rgfs/",
                    "rgfs/rgfs",
                    "rgfs/"
                });

            this.ExpectBlobCount(this.Enlistment.Prefetch("--folders-list " + tempFilePath), 279);
            File.Delete(tempFilePath);
        }

        private void ExpectBlobCount(string output, int expectedCount)
        {
            output.ShouldContain("Matched blobs:    " + expectedCount);
        }
    }
}
