using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Sparse;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.Git;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class WorktreeEnlistmentTests
    {
        // SHA-256 of "primaryenlistmentid:agent-wt-1", first 16 bytes as lowercase hex.
        private const string DerivedEnlistmentIdOfAgentWorktree = "c133def22e85af1a8e16432fc7684258";

        private string testRoot;
        private string primaryRoot;
        private string sharedGitDir;
        private string worktreePath;
        private string worktreeGitDir;
        private string unattendedValue;

        [SetUp]
        public void SetUp()
        {
            // The required settings depend on GVFS_UNATTENDED, and git config file
            // arguments must handle a path with a space.
            this.unattendedValue = Environment.GetEnvironmentVariable(GVFSConstants.UnattendedEnvironmentVariable);
            Environment.SetEnvironmentVariable(GVFSConstants.UnattendedEnvironmentVariable, null);

            this.testRoot = Path.Combine(Path.GetTempPath(), "GVFS WT Enl Tests_" + Path.GetRandomFileName());
            this.primaryRoot = Path.Combine(this.testRoot, "enlistment");
            string primarySrc = Path.Combine(this.primaryRoot, "src");
            this.sharedGitDir = Path.Combine(primarySrc, ".git");
            this.worktreePath = Path.Combine(this.testRoot, "agent-wt-1");
            this.worktreeGitDir = Path.Combine(this.sharedGitDir, "worktrees", "agent-wt-1");

            Directory.CreateDirectory(this.sharedGitDir);
            Directory.CreateDirectory(Path.Combine(this.sharedGitDir, "objects"));
            Directory.CreateDirectory(Path.Combine(this.sharedGitDir, "refs"));
            Directory.CreateDirectory(this.worktreeGitDir);
            Directory.CreateDirectory(this.worktreePath);
            Directory.CreateDirectory(Path.Combine(this.primaryRoot, ".gvfs"));

            File.WriteAllText(
                Path.Combine(this.sharedGitDir, "config"),
                "[core]\n\trepositoryformatversion = 0\n[remote \"origin\"]\n\turl = https://mock/repo\n");
            File.WriteAllText(
                Path.Combine(this.sharedGitDir, "HEAD"),
                "ref: refs/heads/main\n");
            File.WriteAllText(
                Path.Combine(this.worktreePath, ".git"),
                "gitdir: " + this.worktreeGitDir);
            File.WriteAllText(
                Path.Combine(this.worktreeGitDir, "commondir"),
                "../..");
            File.WriteAllText(
                Path.Combine(this.worktreeGitDir, "HEAD"),
                "ref: refs/heads/agent-wt-1\n");
            File.WriteAllText(
                Path.Combine(this.worktreeGitDir, "gitdir"),
                Path.Combine(this.worktreePath, ".git"));
        }

        [TearDown]
        public void TearDown()
        {
            Environment.SetEnvironmentVariable(GVFSConstants.UnattendedEnvironmentVariable, this.unattendedValue);
            if (Directory.Exists(this.testRoot))
            {
                Directory.Delete(this.testRoot, recursive: true);
            }
        }

        private GVFSEnlistment CreateWorktreeEnlistment(string gitBinPath = @"mock:\git")
        {
            return GVFSEnlistment.CreateForWorktree(
                this.primaryRoot, gitBinPath, authentication: null,
                GVFSEnlistment.TryGetWorktreeInfo(this.worktreePath),
                repoUrl: "https://mock/repo");
        }

        [TestCase]
        public void IsWorktreeReturnsTrueForWorktreeEnlistment()
        {
            GVFSEnlistment enlistment = this.CreateWorktreeEnlistment();
            enlistment.IsWorktree.ShouldBeTrue();
        }

        [TestCase]
        public void SparseCheckoutFileIsInTheWorktreeGitDir()
        {
            GVFSEnlistment enlistment = this.CreateWorktreeEnlistment();

            SparseCheckoutPathResolver.GetSparseCheckoutFilePath(enlistment)
                .ShouldEqual(Path.Combine(this.worktreeGitDir, "info", "sparse-checkout"));
        }

        [TestCase]
        public void WorktreeInfoIsPopulated()
        {
            GVFSEnlistment enlistment = this.CreateWorktreeEnlistment();
            enlistment.Worktree.ShouldNotBeNull();
            enlistment.Worktree.Name.ShouldEqual("agent-wt-1");
            enlistment.Worktree.WorktreePath.ShouldEqual(this.worktreePath);
        }

        [TestCase]
        public void DotGitRootPointsToSharedGitDir()
        {
            GVFSEnlistment enlistment = this.CreateWorktreeEnlistment();
            enlistment.DotGitRoot.ShouldEqual(this.sharedGitDir);
        }

        [TestCase]
        public void GitProcessPassesWorktreeGitFileAsGitDir()
        {
            // The --git-dir that reaches git must be the worktree's .git file, not the gitdir:
            // target inside it and not DotGitRoot. Git resolves per-worktree state through the file.
            GVFSEnlistment enlistment = this.CreateWorktreeEnlistment();
            MockGitProcess gitProcess = new MockGitProcess(enlistment);
            gitProcess.SetExpectedCommandResult(
                "rev-parse HEAD",
                () => new GitProcess.Result("abc123", string.Empty, GitProcess.Result.SuccessCode));

            gitProcess.RevParse("HEAD");

            gitProcess.InvocationsRun.Count.ShouldEqual(1);
            string gitDir = gitProcess.InvocationsRun[0].DotGitDirectory;
            gitDir.ShouldEqual(Path.Combine(this.worktreePath, ".git"));
            gitDir.ShouldNotEqual(enlistment.DotGitRoot);
            gitDir.ShouldNotEqual(this.worktreeGitDir);
        }

        [TestCase]
        public void WorkingDirectoryRootIsWorktreePath()
        {
            GVFSEnlistment enlistment = this.CreateWorktreeEnlistment();
            enlistment.WorkingDirectoryRoot.ShouldEqual(this.worktreePath);
        }

        [TestCase]
        public void LocalObjectsRootIsSharedGitObjects()
        {
            GVFSEnlistment enlistment = this.CreateWorktreeEnlistment();
            enlistment.LocalObjectsRoot.ShouldEqual(
                Path.Combine(this.sharedGitDir, "objects"));
        }

        [TestCase]
        public void LocalObjectsRootDoesNotDoubleGitPath()
        {
            GVFSEnlistment enlistment = this.CreateWorktreeEnlistment();
            Assert.IsFalse(
                enlistment.LocalObjectsRoot.Contains(Path.Combine(".git", ".git")),
                "LocalObjectsRoot should not have doubled .git path");
        }

        [TestCase]
        public void GitIndexPathUsesWorktreeGitDir()
        {
            GVFSEnlistment enlistment = this.CreateWorktreeEnlistment();
            enlistment.GitIndexPath.ShouldEqual(
                Path.Combine(this.worktreeGitDir, "index"));
        }

        [TestCase]
        public void GitHeadLogPathUsesWorktreeGitDir()
        {
            GVFSEnlistment enlistment = this.CreateWorktreeEnlistment();
            enlistment.GitHeadLogPath.ShouldEqual(
                Path.Combine(this.worktreeGitDir, GVFSConstants.DotGit.Logs.HeadRelativePath));
        }

        [TestCase]
        public void NamedPipeNameIncludesWorktreeSuffix()
        {
            GVFSEnlistment enlistment = this.CreateWorktreeEnlistment();
            Assert.IsTrue(
                enlistment.NamedPipeName.Contains("_WT_AGENT-WT-1"),
                "NamedPipeName should contain worktree suffix");
        }

        [TestCase]
        public void DotGVFSRootIsInWorktreeGitDir()
        {
            GVFSEnlistment enlistment = this.CreateWorktreeEnlistment();
            Assert.IsTrue(
                enlistment.DotGVFSRoot.Contains(this.worktreeGitDir),
                "DotGVFSRoot should be inside worktree git dir");
        }

        [TestCase]
        public void EnlistmentRootIsPrimaryRoot()
        {
            GVFSEnlistment enlistment = this.CreateWorktreeEnlistment();
            enlistment.PrimaryEnlistmentRoot.ShouldEqual(this.primaryRoot);
        }

        [TestCase]
        public void RepoUrlIsReadFromSharedConfig()
        {
            GVFSEnlistment enlistment = this.CreateWorktreeEnlistment();
            enlistment.RepoUrl.ShouldEqual("https://mock/repo");
        }

        [TestCase]
        public void WorktreeEnlistmentIdIsEmptyWhenNothingIsPersisted()
        {
            this.AppendToSharedConfig("[gvfs]\n\tenlistment-id = primaryenlistmentid\n");
            GVFSEnlistment enlistment = this.CreateWorktreeEnlistment(GetGitBinPath());

            enlistment.GetEnlistmentId().ShouldEqual(string.Empty);
            File.Exists(GetRepoMetadataPath(enlistment.DotGVFSRoot)).ShouldBeFalse("Reading the id must not create worktree repo metadata");
            File.Exists(GetRepoMetadataPath(this.PrimaryDotGVFSRoot)).ShouldBeFalse("Reading the id must not create primary repo metadata");
        }

        [TestCase]
        public void WorktreeEnlistmentIdIsDerivedFromPrimaryRepoMetadata()
        {
            this.AppendToSharedConfig("[gvfs]\n\tenlistment-id = sharedconfigenlistmentid\n");
            EditRepoMetadata(this.PrimaryDotGVFSRoot, metadata => metadata.SetEntry(RepoMetadata.Keys.EnlistmentId, "primaryenlistmentid"));
            GVFSEnlistment enlistment = this.CreateWorktreeEnlistment(GetGitBinPath());

            enlistment.GetEnlistmentId().ShouldEqual(DerivedEnlistmentIdOfAgentWorktree);
            this.CreateWorktreeEnlistment(GetGitBinPath()).GetEnlistmentId().ShouldEqual(DerivedEnlistmentIdOfAgentWorktree);
            RepoMetadata.Instance.ShouldBeNull("Reading the id must not initialize RepoMetadata.Instance");
        }

        [TestCase]
        public void WorktreeEnlistmentIdIgnoresCaseOfWorktreeName()
        {
            GVFSEnlistment.CreateWorktreeEnlistmentId("primaryenlistmentid", "AGENT-WT-1").ShouldEqual(DerivedEnlistmentIdOfAgentWorktree);
        }

        [TestCase]
        public void WorktreeEnlistmentIdFallsBackToWorktreeRepoMetadataWithoutPrimaryId()
        {
            GVFSEnlistment enlistment = this.CreateWorktreeEnlistment();
            EditRepoMetadata(enlistment.DotGVFSRoot, metadata => metadata.SetEntry(RepoMetadata.Keys.EnlistmentId, "storedworktreeid"));

            enlistment.GetEnlistmentId().ShouldEqual("storedworktreeid");
        }

        [TestCase]
        public void WorktreeEnlistmentIdFallsBackToWorktreeRepoMetadataWhenPrimaryMetadataIsLocked()
        {
            EditRepoMetadata(this.PrimaryDotGVFSRoot, metadata => metadata.SetEntry(RepoMetadata.Keys.EnlistmentId, "primaryenlistmentid"));
            GVFSEnlistment enlistment = this.CreateWorktreeEnlistment();
            EditRepoMetadata(enlistment.DotGVFSRoot, metadata => metadata.SetEntry(RepoMetadata.Keys.EnlistmentId, "storedworktreeid"));

            using (new FileStream(GetRepoMetadataPath(this.PrimaryDotGVFSRoot), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                enlistment.GetEnlistmentId().ShouldEqual("storedworktreeid");
            }
        }

        [TestCase]
        public void PrimaryEnlistmentIdIsReadWhenLockIsReleasedDuringRetry()
        {
            EditRepoMetadata(this.PrimaryDotGVFSRoot, metadata => metadata.SetEntry(RepoMetadata.Keys.EnlistmentId, "primaryenlistmentid"));
            GVFSEnlistment enlistment = this.CreateWorktreeEnlistment();

            FileStream lockStream = new FileStream(GetRepoMetadataPath(this.PrimaryDotGVFSRoot), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            using (System.Threading.Timer releaseTimer = new System.Threading.Timer(_ => lockStream.Dispose(), null, 20, System.Threading.Timeout.Infinite))
            {
                enlistment.TryGetPrimaryEnlistmentId(out string primaryEnlistmentId, out string error).ShouldBeTrue(error);
                primaryEnlistmentId.ShouldEqual("primaryenlistmentid");
            }

            lockStream.Dispose();
        }

        [TestCase("givenid", "storedid", "givenid")]
        [TestCase("", "storedid", "storedid")]
        [TestCase(null, "storedid", "storedid")]
        public void WorktreeMetadataKeepsGivenOrStoredEnlistmentId(string givenId, string storedId, string expectedId)
        {
            GVFSEnlistment enlistment = this.CreateWorktreeEnlistment();
            EditRepoMetadata(enlistment.DotGVFSRoot, metadata =>
            {
                metadata.SetEntry(RepoMetadata.Keys.EnlistmentId, storedId);
                MockTracer tracer = new MockTracer();

                metadata.GetWorktreeEnlistmentIdToSave(tracer, givenId).ShouldEqual(expectedId);
                tracer.RelatedEventNames.ShouldNotContain(name => name == "CreateNewEnlistmentId");
            });
        }

        [TestCase(null)]
        [TestCase(" ")]
        public void WorktreeMetadataCreatesEnlistmentIdWhenNoneIsGivenOrStored(string storedId)
        {
            GVFSEnlistment enlistment = this.CreateWorktreeEnlistment();
            EditRepoMetadata(enlistment.DotGVFSRoot, metadata =>
            {
                if (storedId != null)
                {
                    metadata.SetEntry(RepoMetadata.Keys.EnlistmentId, storedId);
                }

                MockTracer tracer = new MockTracer();
                string enlistmentId = metadata.GetWorktreeEnlistmentIdToSave(tracer, " ");

                Guid.TryParseExact(enlistmentId, "N", out _).ShouldBeTrue(enlistmentId);
                tracer.RelatedEventNames.ShouldContain(name => name == "CreateNewEnlistmentId");
            });
        }

        [TestCase]
        public void RequiredSettingsEnableWorktreeConfigAndWritePerWorktreeValues()
        {
            GVFSEnlistment enlistment = this.CreateWorktreeEnlistment(GetGitBinPath());

            string error;
            RequiredGitConfig.TrySetRequiredSettings(
                enlistment,
                new Dictionary<string, string>
                {
                    { "status.deserializePath", "C:/worktree/status.dat" },
                    { "core.autocrlf", "false" },
                },
                out error).ShouldBeTrue(error);

            this.GetConfigValues(this.SharedConfigPath, "extensions.worktreeConfig").ShouldEqual("true");
            this.GetConfigValues(this.SharedConfigPath, "core.autocrlf").ShouldEqual("false");
            this.GetConfigValues(this.SharedConfigPath, "status.deserializePath").ShouldEqual(this.PrimaryStatusCachePath);
            this.GetConfigValues(this.WorktreeConfigPath, "status.deserializePath").ShouldEqual("C:/worktree/status.dat");
        }

        [TestCase]
        public void RequiredSettingsRepairCopiedWorktreeValueAndSharedValueOfPrimary()
        {
            this.AppendToSharedConfig("[status]\n\tdeserializePath = C:/sibling/status.dat\n");
            File.WriteAllText(this.WorktreeConfigPath, "[status]\n\tdeserializePath = C:/sibling/status.dat\n");
            GVFSEnlistment enlistment = this.CreateWorktreeEnlistment(GetGitBinPath());

            string error;
            RequiredGitConfig.TrySetRequiredSettings(
                enlistment,
                new Dictionary<string, string> { { "status.deserializePath", "C:/worktree/status.dat" } },
                out error).ShouldBeTrue(error);

            this.GetConfigValues(this.WorktreeConfigPath, "status.deserializePath").ShouldEqual("C:/worktree/status.dat");
            this.GetConfigValues(this.SharedConfigPath, "status.deserializePath").ShouldEqual(this.PrimaryStatusCachePath);
        }

        [TestCase]
        public void RequiredSettingsDisableNullPerWorktreeValueInWorktreeConfig()
        {
            this.AppendToSharedConfig("[extensions]\n\tworktreeConfig = true\n[status]\n\tdeserializePath = C:/primary/status.dat\n");
            File.WriteAllText(this.WorktreeConfigPath, "[status]\n\tdeserializePath = C:/worktree/status.dat\n");
            GVFSEnlistment enlistment = this.CreateWorktreeEnlistment(GetGitBinPath());

            string error;
            RequiredGitConfig.TrySetRequiredSettings(
                enlistment,
                new Dictionary<string, string> { { "status.deserializePath", null } },
                out error).ShouldBeTrue(error);

            this.GetConfigValues(this.WorktreeConfigPath, "status.deserializePath").ShouldEqual(string.Empty);
            File.ReadAllText(this.WorktreeConfigPath).ShouldContain("deserializePath");
            this.GetConfigValues(this.SharedConfigPath, "status.deserializePath").ShouldEqual("C:/primary/status.dat");
        }

        [TestCase]
        public void RequiredSettingsRemoveRequiredOverridesFromWorktreeConfig()
        {
            File.WriteAllText(this.WorktreeConfigPath, "[core]\n\tautocrlf = true\n[gvfs]\n\tmount-id = worktreemountid\n");
            GVFSEnlistment enlistment = this.CreateWorktreeEnlistment(GetGitBinPath());

            string error;
            RequiredGitConfig.TrySetRequiredSettings(
                enlistment,
                new Dictionary<string, string> { { "core.autocrlf", "false" } },
                out error).ShouldBeTrue(error);

            this.GetConfigValues(this.WorktreeConfigPath, "core.autocrlf").ShouldEqual(string.Empty);
            this.GetConfigValues(this.WorktreeConfigPath, "gvfs.mount-id").ShouldEqual("worktreemountid");
            this.GetConfigValues(this.SharedConfigPath, "core.autocrlf").ShouldEqual("false");
        }

        [TestCase]
        public void RequiredSettingsReplaceDuplicateWorktreeConfigExtensionValues()
        {
            this.AppendToSharedConfig("[extensions]\n\tworktreeConfig = true\n\tworktreeConfig = false\n");
            GVFSEnlistment enlistment = this.CreateWorktreeEnlistment(GetGitBinPath());

            string error;
            RequiredGitConfig.TrySetRequiredSettings(enlistment, new Dictionary<string, string>(), out error).ShouldBeTrue(error);

            this.GetConfigValues(this.SharedConfigPath, "extensions.worktreeConfig").ShouldEqual("true");
        }

        [TestCase]
        public void PrimaryRequiredSettingsEnableWorktreeConfigAndRemoveOverridesWhenLinkedWorktreeHasConfig()
        {
            File.WriteAllText(this.WorktreeConfigPath, "[status]\n\tdeserializePath = C:/worktree/status.dat\n");
            string primaryWorktreeConfigPath = Path.Combine(this.sharedGitDir, "config.worktree");
            File.WriteAllText(primaryWorktreeConfigPath, "[core]\n\tautocrlf = true\n");
            GVFSEnlistment primary = new GVFSEnlistment(this.primaryRoot, "https://mock/repo", GetGitBinPath(), authentication: null);

            string error;
            RequiredGitConfig.TrySetRequiredSettings(
                primary,
                new Dictionary<string, string> { { "core.autocrlf", "false" } },
                out error).ShouldBeTrue(error);

            this.GetConfigValues(this.SharedConfigPath, "extensions.worktreeConfig").ShouldEqual("true");
            this.GetConfigValues(primaryWorktreeConfigPath, "core.autocrlf").ShouldEqual(string.Empty);
            this.GetConfigValues(this.WorktreeConfigPath, "status.deserializePath").ShouldEqual("C:/worktree/status.dat");
        }

        [TestCase]
        public void PrimaryRequiredSettingsDoNotEnableWorktreeConfigWithoutLinkedWorktreeConfig()
        {
            GVFSEnlistment primary = new GVFSEnlistment(this.primaryRoot, "https://mock/repo", GetGitBinPath(), authentication: null);

            string error;
            RequiredGitConfig.TrySetRequiredSettings(
                primary,
                new Dictionary<string, string> { { "status.deserializePath", "C:/primary/status.dat" } },
                out error).ShouldBeTrue(error);

            this.GetConfigValues(this.SharedConfigPath, "extensions.worktreeConfig").ShouldEqual(string.Empty);
            this.GetConfigValues(this.SharedConfigPath, "status.deserializePath").ShouldEqual("C:/primary/status.dat");
            File.Exists(Path.Combine(this.sharedGitDir, "config.worktree")).ShouldBeFalse();
        }

        [TestCase]
        public void RequiredSettingsWriteNothingWhenAppliedAgain()
        {
            GVFSEnlistment primary = new GVFSEnlistment(this.primaryRoot, "https://mock/repo", GetGitBinPath(), authentication: null);
            GVFSEnlistment worktree = this.CreateWorktreeEnlistment(GetGitBinPath());

            string error;
            RequiredGitConfig.TrySetRequiredSettings(primary, out error).ShouldBeTrue(error);
            RequiredGitConfig.TrySetRequiredSettings(worktree, out error).ShouldBeTrue(error);
            DateTime sharedConfigWriteTime = File.GetLastWriteTimeUtc(this.SharedConfigPath);
            DateTime worktreeConfigWriteTime = File.GetLastWriteTimeUtc(this.WorktreeConfigPath);

            RequiredGitConfig.TrySetRequiredSettings(primary, out error).ShouldBeTrue(error);
            RequiredGitConfig.TrySetRequiredSettings(worktree, out error).ShouldBeTrue(error);

            File.GetLastWriteTimeUtc(this.SharedConfigPath).ShouldEqual(sharedConfigWriteTime, "A second application wrote the shared config");
            File.GetLastWriteTimeUtc(this.WorktreeConfigPath).ShouldEqual(worktreeConfigWriteTime, "A second application wrote config.worktree");
            this.GetConfigValues(this.SharedConfigPath, "credential.https://dev.azure.com.useHttpPath").ShouldEqual("true");
        }

        [TestCase]
        public void RequiredSettingsFailWhenWorktreeConfigIsCorrupt()
        {
            File.WriteAllText(this.WorktreeConfigPath, "[status\n");
            GVFSEnlistment enlistment = this.CreateWorktreeEnlistment(GetGitBinPath());

            string error;
            RequiredGitConfig.TrySetRequiredSettings(
                enlistment,
                new Dictionary<string, string> { { "status.deserializePath", "C:/worktree/status.dat" } },
                out error).ShouldBeFalse();

            error.ShouldContain("config.worktree");
        }

        private string PrimaryDotGVFSRoot => Path.Combine(this.primaryRoot, GVFSPlatform.Instance.Constants.DotGVFSRoot);

        private string PrimaryStatusCachePath => Path.Combine(this.PrimaryDotGVFSRoot, "gitStatusCache", "GitStatusCache.dat").Replace('\\', '/');

        private string SharedConfigPath => Path.Combine(this.sharedGitDir, "config");

        private string WorktreeConfigPath => Path.Combine(this.worktreeGitDir, "config.worktree");

        private static string GetGitBinPath()
        {
            string gitPath = GitTestHelper.FindGitOnPath();
            Assert.That(gitPath, Is.Not.Null, "The required-settings tests require Git on PATH.");
            return gitPath;
        }

        private static string GetRepoMetadataPath(string dotGVFSRoot)
        {
            return Path.Combine(dotGVFSRoot, GVFSConstants.DotGVFS.Databases.RepoMetadata);
        }

        private static void EditRepoMetadata(string dotGVFSRoot, Action<RepoMetadata> action)
        {
            string error;
            RepoMetadata.TryInitialize(new MockTracer(), new OverwritingFileSystem(), dotGVFSRoot, out error).ShouldBeTrue(error);
            try
            {
                action(RepoMetadata.Instance);
            }
            finally
            {
                RepoMetadata.Shutdown();
            }
        }

        /// <summary>
        /// Returns every value of <paramref name="key"/> in <paramref name="configFile"/>,
        /// one per line, or an empty string when the key is missing.
        /// </summary>
        private string GetConfigValues(string configFile, string key)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo(GetGitBinPath(), $"config --file \"{configFile}\" --get-all {key}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            using (Process process = Process.Start(startInfo))
            {
                string output = process.StandardOutput.ReadToEnd();
                string errors = process.StandardError.ReadToEnd();
                process.WaitForExit();
                (process.ExitCode == 0 || process.ExitCode == 1).ShouldBeTrue(errors);
                return output.Trim();
            }
        }

        private void AppendToSharedConfig(string text)
        {
            File.AppendAllText(this.SharedConfigPath, text);
        }

        // The unit test platform does not support MoveAndOverwriteFile, which
        // FileBasedDictionary uses to replace its data file.
        private class OverwritingFileSystem : PhysicalFileSystem
        {
            public override void MoveAndOverwriteFile(string sourceFileName, string destinationFilename)
            {
                File.Move(sourceFileName, destinationFilename, overwrite: true);
            }
        }
    }
}
