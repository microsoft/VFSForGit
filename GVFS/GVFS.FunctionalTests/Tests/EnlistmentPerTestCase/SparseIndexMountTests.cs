using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerTestCase
{
    [TestFixture]
    public class SparseIndexMountTests : TestsWithEnlistmentPerTestCase
    {
        private const string BypassGVFS = "-c core.virtualfilesystem= -c core.hookspath=";
        private const string ConeMode = "-c core.sparseCheckout=true -c core.sparseCheckoutCone=true";

        [TestCase]
        public void MountRejectsSparseIndexAndRecoveryStepsWork()
        {
            string fullIndexEntries = GitProcess.Invoke(this.Enlistment.RepoRoot, "ls-files -s");

            this.Enlistment.UnmountGVFS();

            // Turn on the sparse index in the repository config, and collapse every top-level
            // folder into a sparse-directory entry. The persisted projection gets the same index,
            // as it does when git collapses the index while the repository is mounted.
            this.GitShouldSucceed("-c core.hookspath= config index.sparse true");
            string sparseCheckoutPath = this.Enlistment.GetDotGitPath("info", "sparse-checkout");
            string originalSparseCheckout = File.Exists(sparseCheckoutPath) ? File.ReadAllText(sparseCheckoutPath) : null;
            File.WriteAllText(sparseCheckoutPath, "/*\n!/*/\n");
            this.GitShouldSucceed($"{BypassGVFS} {ConeMode} -c sparse.expectFilesOutsideOfPatterns=true update-index --force-write-index");
            string[] collapsedFolders = this.GitShouldSucceed($"{BypassGVFS} {ConeMode} ls-files --sparse")
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.EndsWith("/"))
                .ToArray();
            collapsedFolders.ShouldBeNonEmpty();
            if (originalSparseCheckout == null)
            {
                File.Delete(sparseCheckoutPath);
            }
            else
            {
                File.WriteAllText(sparseCheckoutPath, originalSparseCheckout);
            }

            File.Copy(this.Enlistment.GetDotGitPath("index"), Path.Combine(this.Enlistment.DotGVFSRoot, "GVFS_projection"), overwrite: true);

            this.Enlistment.TryMountGVFS(out string output).ShouldBeFalse("Mount must fail for a sparse index");
            output.ShouldContain("The git index contains the sparse-directory entry", "1. If the enlistment is mounted, run 'gvfs unmount'.", "5. Run 'gvfs mount'.");
            output.ShouldNotContain(ignoreCase: false, "InvalidDataException", "SparseIndexNotSupportedException", "   at GVFS.");

            // The mount failed, so step 1 does not apply. Do steps 2, 3, and 4 as printed.
            Match configStep = Regex.Match(output, @"2\. In '(?<directory>[^']+)', run 'git (?<arguments>[^']+)'\.");
            configStep.Success.ShouldBeTrue("Output does not contain step 2: " + output);
            Match expandStep = Regex.Match(output, @"3\. In '(?<directory>[^']+)', run 'git (?<arguments>[^']+)'\.");
            expandStep.Success.ShouldBeTrue("Output does not contain step 3: " + output);
            Match deleteStep = Regex.Match(output, @"4\. Delete '(?<file>[^']+)'\.");
            deleteStep.Success.ShouldBeTrue("Output does not contain step 4: " + output);

            this.GitShouldSucceed(configStep.Groups["arguments"].Value, configStep.Groups["directory"].Value);
            this.GitShouldSucceed(expandStep.Groups["arguments"].Value, expandStep.Groups["directory"].Value);
            File.Exists(deleteStep.Groups["file"].Value).ShouldBeTrue("Step 4 names a file that does not exist: " + deleteStep.Groups["file"].Value);
            File.Delete(deleteStep.Groups["file"].Value);

            this.Enlistment.MountGVFS();

            this.GitShouldSucceed("config index.sparse").Trim().ShouldEqual("false");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "ls-files -s").ShouldEqual(fullIndexEntries);
            string fileInCollapsedFolder = fullIndexEntries
                .Split('\n')
                .Select(line => line.Split('\t').Last().Trim())
                .First(path => collapsedFolders.Any(folder => path.StartsWith(folder)));
            File.Exists(this.Enlistment.GetVirtualPathTo(fileInCollapsedFolder)).ShouldBeTrue("Projection is missing " + fileInCollapsedFolder);
        }

        private string GitShouldSucceed(string command, string workingDirectory = null)
        {
            ProcessResult result = GitProcess.InvokeProcess(workingDirectory ?? this.Enlistment.RepoRoot, command);
            result.ExitCode.ShouldEqual(0, $"git {command} failed: {result.Errors}");
            return result.Output;
        }
    }
}
