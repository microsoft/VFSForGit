using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    /// <summary>
    /// Functional tests for the automatic sparse index with a Git that does not report
    /// <c>feature: vfs-sparse-index</c>. The mount refuses such an enlistment, and the recovery
    /// steps in its error make the enlistment work with that Git again.
    /// </summary>
    /// <remarks>
    /// These tests run only with such a Git, which is the Git of the PR validation build.
    /// <see cref="AutoSparseIndexConeTests"/> runs only with a Git that has the feature.
    /// </remarks>
    [TestFixture]
    public class AutoSparseIndexWithoutGitSupportTests : TestsWithEnlistmentPerFixture
    {
        private const string SparseIndexFeature = "feature: vfs-sparse-index";
        private const string WidenEvent = "ConeWiden {";
        private const string CommitIdentity = "-c user.name=Test -c user.email=test@example.com";
        private const string RootOnlyCone = "/*\n!/*/\n";

        // The recovery steps in the mount error. The test runs them as they are printed.
        private static readonly string[] RecoveryGitCommands =
        {
            "-c core.hookspath= config --unset-all gvfs.auto-sparse-index",
            "-c core.hookspath= config --unset-all index.sparse",
            "-c core.hookspath= config core.sparseCheckout false",
        };

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        // Files three or more levels deep, each in a different top-level directory
        private string[] deepFiles;
        private int fullEntryCount;

        [OneTimeSetUp]
        public void SkipWithAGitThatSupportsTheSparseIndex()
        {
            string buildOptions = GitProcess.Invoke(this.Enlistment.RepoRoot, "version --build-options");
            if (buildOptions.Contains(SparseIndexFeature))
            {
                Assert.Ignore("This git reports 'feature: vfs-sparse-index'. AutoSparseIndexConeTests cover that git.");
            }

            string[] headFiles = this.GitShouldSucceed("-c core.quotepath=false ls-tree -r --name-only HEAD")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(path => path.Trim())
                .ToArray();
            this.fullEntryCount = headFiles.Length;
            this.deepFiles = headFiles
                .Where(path => path.Count(character => character == '/') >= 2 && !path.Contains(' '))
                .GroupBy(path => path.Substring(0, path.IndexOf('/')), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Take(2)
                .ToArray();
            this.deepFiles.Length.ShouldEqual(2, "HEAD has too few top-level directories with deep files");
        }

        private string SparseCheckoutPath => this.Enlistment.GetDotGitPath("info", "sparse-checkout");

        [TestCase]
        public void MountRefusesTheSparseIndexAndTheRecoveryStepsWork()
        {
            // The settings and the cone file that turn the feature on.
            this.Enlistment.UnmountGVFS();
            this.GitShouldSucceed("-c core.hookspath= config gvfs.auto-sparse-index true");
            this.GitShouldSucceed("-c core.hookspath= config core.sparseCheckout true");
            this.GitShouldSucceed("-c core.hookspath= config core.sparseCheckoutCone true");
            this.GitShouldSucceed("-c core.hookspath= config index.sparse true");
            File.WriteAllText(this.SparseCheckoutPath, RootOnlyCone, Utf8NoBom);

            // A sparse index on disk, as a Git without the feature can write it. The persisted
            // projection gets the same index, as it does when Git writes the index while the
            // repository is mounted.
            this.GitShouldSucceed("-c core.virtualfilesystem= -c core.hookspath= -c sparse.expectFilesOutsideOfPatterns=true update-index --force-write-index");
            int sparseEntryCount = GVFSHelpers.IndexHeaderEntryCount(this.Enlistment);
            (sparseEntryCount < this.fullEntryCount).ShouldBeTrue($"git did not write a sparse index: {sparseEntryCount} of {this.fullEntryCount} entries");
            File.Copy(this.Enlistment.GetDotGitPath("index"), Path.Combine(this.Enlistment.DotGVFSRoot, "GVFS_projection"), overwrite: true);

            this.Enlistment.TryMountGVFS(out string mountOutput).ShouldBeFalse("The mount accepted a git without the sparse-index feature: " + mountOutput);
            mountOutput.ShouldContain(
                "does not report '" + SparseIndexFeature + "'",
                "gvfs.auto-sparse-index is true",
                "is a cone-mode sparse-checkout file",
                "del \"");
            mountOutput.ShouldContain(RecoveryGitCommands.Select(command => "git " + command).ToArray());

            foreach (string command in RecoveryGitCommands)
            {
                ProcessResult result = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, command);

                // "git config --unset-all" exits with 5 when the key is not set.
                (result.ExitCode == 0 || result.ExitCode == 5).ShouldBeTrue($"git {command} failed: {result.Errors}");
            }

            File.Delete(this.SparseCheckoutPath);

            // The index is still sparse, so the projection refuses it and gives its own steps.
            this.Enlistment.TryMountGVFS(out mountOutput).ShouldBeFalse("The mount accepted a sparse index with the feature off: " + mountOutput);
            mountOutput.ShouldContain(
                "contains the sparse-directory entry",
                "git -c core.virtualfilesystem= -c core.hookspath= -c index.sparse=false update-index --force-write-index");
            this.GitShouldSucceed("-c core.virtualfilesystem= -c core.hookspath= -c index.sparse=false update-index --force-write-index");
            File.Delete(Path.Combine(this.Enlistment.DotGVFSRoot, "GVFS_projection"));

            this.Enlistment.MountGVFS();

            this.GitShouldSucceed("status");
            this.ModifyAddAndCommit(this.deepFiles[0]);
            this.StatusShouldBeClean();
            GVFSHelpers.IndexHeaderEntryCount(this.Enlistment).ShouldEqual(this.fullEntryCount, "The index on disk is not full");
            File.Exists(this.SparseCheckoutPath).ShouldBeFalse("The mount created a sparse-checkout file");
            this.ReadMountLog().ShouldNotContain(ignoreCase: false, WidenEvent);
        }

        [TestCase]
        public void MountAcceptsALegacySparseCheckoutFile()
        {
            // Versions of VFS for Git before 2018 set core.sparseCheckout to true and wrote this
            // file. Such enlistments mount with any supported Git.
            this.Enlistment.UnmountGVFS();
            this.GitShouldSucceed("-c core.hookspath= config core.sparseCheckout true");
            File.WriteAllText(this.SparseCheckoutPath, "/.gitattributes\r\n", Utf8NoBom);

            this.Enlistment.MountGVFS();

            this.ModifyAddAndCommit(this.deepFiles[1]);
            this.StatusShouldBeClean();
            File.ReadAllText(this.SparseCheckoutPath).ShouldEqual("/.gitattributes\r\n");
        }

        private void ModifyAddAndCommit(string gitPath)
        {
            File.AppendAllText(this.Enlistment.GetVirtualPathTo(gitPath), Environment.NewLine + "modified by the test");

            ProcessResult add = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, $"add -- {gitPath}");
            add.ExitCode.ShouldEqual(0, add.Errors);
            string staged = this.GitShouldSucceed("diff --cached --name-only");
            staged.Contains(gitPath).ShouldBeTrue($"git add did not stage {gitPath}. Errors: {add.Errors}");

            this.GitShouldSucceed($"{CommitIdentity} commit -m \"Change {gitPath}\"");
            this.GitShouldSucceed("log -1 --name-only --format=").ShouldContain(gitPath);
        }

        private void StatusShouldBeClean()
        {
            string status = this.GitShouldSucceed("status --porcelain");
            string.IsNullOrWhiteSpace(status).ShouldBeTrue("git status is not clean: " + status);
        }

        private string ReadMountLog()
        {
            return Directory.GetFiles(this.Enlistment.GVFSLogsRoot, "gvfs_mount_process_*.log")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Select(GVFSHelpers.ReadAllTextFromWriteLockedFile)
                .First();
        }

        private string GitShouldSucceed(string command)
        {
            ProcessResult result = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, command);
            result.ExitCode.ShouldEqual(0, $"git {command} failed: {result.Errors}");
            return result.Output;
        }
    }
}
