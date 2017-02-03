using GVFS.FunctionalTests.Category;
using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerTestCase
{
    [TestFixture]
    [Category(CategoryConstants.FastFetch)]
    public class PrefetchVerbTests : TestsWithEnlistmentPerTestCase
    {
        [TestCaseSource(typeof(FileSystemRunner), FileSystemRunner.TestRunners)]
        public void PrefetchFetchesAtRootLevel(FileSystemRunner fileSystem)
        {
            // Root-level files and folders starting with R (currently just Readme.md) and gvflt\gvflt.nuspec
            string output = this.Enlistment.PrefetchFolder("R;gvflt_fileeatest\\oneeaattributewillpass.txt");
            output.ShouldContain("\"TotalMissingObjects\":2");

            // Note: It is expected to always have .gitattributes
            HashSet<string> expectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".gitattributes",
                "Readme.md",
                "GVFlt_FileEATest/OneEAAttributeWillPass.txt"
            };

            this.AllFetchedFilePathsShouldPassCheck(expectedFiles.Contains);
        }

        [TestCaseSource(typeof(FileSystemRunner), FileSystemRunner.TestRunners)]
        public void PrefetchIsAllowedToDoNothing(FileSystemRunner fileSystem)
        {
            string output = this.Enlistment.PrefetchFolder("NoFileHasThisName.IHope");
            output.ShouldContain("\"TotalMissingObjects\":0");

            // It is expected to have .gitattributes files always. But that is all.
            this.AllFetchedFilePathsShouldPassCheck(file => file.Equals(".gitattributes", StringComparison.OrdinalIgnoreCase));
        }

        [TestCaseSource(typeof(FileSystemRunner), FileSystemRunner.TestRunners)]
        public void PrefetchFetchesDirectoriesRecursively(FileSystemRunner fileSystem)
        {
            // Everything under the gvfs folder. Include some duplicates for variety.
            string tempFilePath = Path.Combine(Path.GetTempPath(), "temp.file");
            File.WriteAllLines(tempFilePath, new[] { "gvfs/", "gvfs/gvfs", "gvfs/" });

            string output = this.Enlistment.PrefetchFolderBasedOnFile(tempFilePath);
            File.Delete(tempFilePath);

            output.ShouldContain("\"TotalMissingObjects\":283");

            this.AllFetchedFilePathsShouldPassCheck(file => file.StartsWith("gvfs/", StringComparison.OrdinalIgnoreCase) 
            || file.Equals(".gitattributes", StringComparison.OrdinalIgnoreCase));
        }
                
        private void AllFetchedFilePathsShouldPassCheck(Func<string, bool> checkPath)
        {
            // Form a cache map of sha => path
            string[] allObjects = GitProcess.Invoke(this.Enlistment.RepoRoot, "cat-file --batch-check --batch-all-objects").Split('\n');
            string[] gitlsLines = GitProcess.Invoke(this.Enlistment.RepoRoot, "ls-tree -r HEAD").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            Dictionary<string, List<string>> allPaths = new Dictionary<string, List<string>>();
            foreach (string line in gitlsLines)
            {
                string sha = this.GetShaFromLsLine(line);
                string path = this.GetPathFromLsLine(line);
                if (!allPaths.ContainsKey(sha))
                {
                    allPaths.Add(sha, new List<string>());
                }

                allPaths[sha].Add(path);
            }

            foreach (string sha in allObjects.Where(line => line.Contains(" blob ")).Select(line => line.Substring(0, 40)))
            {
                allPaths.ContainsKey(sha).ShouldEqual(true, "Found a blob that wasn't in the tree: " + sha);

                // A single blob should map to multiple files, so if any pass for a single sha, we have to give a pass.    
                allPaths[sha].Any(path => checkPath(path)).ShouldEqual(true);
            }
        }

        private string GetShaFromLsLine(string line)
        {
            string output = line.Substring(line.LastIndexOf('\t') - 40, 40);
            return output;
        }

        private string GetPathFromLsLine(string line)
        {
            int idx = line.LastIndexOf('\t') + 1;
            string output = line.Substring(idx, line.Length - idx);
            return output;
        }
    }
}
