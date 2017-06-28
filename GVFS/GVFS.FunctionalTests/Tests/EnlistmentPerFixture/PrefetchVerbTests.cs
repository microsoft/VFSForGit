using GVFS.FunctionalTests.Category;
using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.IO;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    [Category(CategoryConstants.FastFetch)]
    [Ignore("Ignoring because these tests are susceptible to a race caused by MSSense scanning files that were accessed in a previous test")]
    public class PrefetchVerbTests : TestsWithEnlistmentPerFixture
    {
        [TestCaseSource(typeof(FileSystemRunner), FileSystemRunner.TestRunners)]
        public void PrefetchFetchesAtRootLevel(FileSystemRunner fileSystem)
        {
            string output = this.Enlistment.PrefetchFolder("R;gvflt_fileeatest\\oneeaattributewillpass.txt");
            output.ShouldContain("\"TotalMissingObjects\":2");
        }

        [TestCaseSource(typeof(FileSystemRunner), FileSystemRunner.TestRunners)]
        public void PrefetchIsAllowedToDoNothing(FileSystemRunner fileSystem)
        {
            string output = this.Enlistment.PrefetchFolder("NoFileHasThisName.IHope");
            output.ShouldContain("\"TotalMissingObjects\":0");
        }

        [TestCaseSource(typeof(FileSystemRunner), FileSystemRunner.TestRunners)]
        public void PrefetchFetchesDirectoriesRecursively(FileSystemRunner fileSystem)
        {
            string tempFilePath = Path.Combine(Path.GetTempPath(), "temp.file");
            File.WriteAllLines(
                tempFilePath,
                new[]
                {
                    "# A comment",
                    " ",
                    "gvfs/",
                    "gvfs/gvfs",
                    "gvfs/"
                });

            string output = this.Enlistment.PrefetchFolderBasedOnFile(tempFilePath);
            File.Delete(tempFilePath);

            output.ShouldContain("\"RequiredBlobsCount\":283");
        }
    }
}
