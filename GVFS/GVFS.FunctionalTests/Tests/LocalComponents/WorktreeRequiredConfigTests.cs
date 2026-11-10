using GVFS.Common;
using GVFS.Common.Git;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using GVFS.UnitTests;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using GitProcess = GVFS.FunctionalTests.Tools.GitProcess;
using ProcessResult = GVFS.FunctionalTests.Tools.ProcessResult;

namespace GVFS.FunctionalTests.Tests.LocalComponents
{
    [TestFixture]
    [NonParallelizable]
    public class WorktreeRequiredConfigTests
    {
        private string testRoot;
        private string primaryRoot;
        private string sharedGitDir;
        private string worktreePath;
        private string worktreeGitDir;
        private string unattendedValue;
        private IsolatedGitEnvironment environment;

        [SetUp]
        public void SetUp()
        {
            this.environment = new IsolatedGitEnvironment();
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
            try
            {
                if (Directory.Exists(this.testRoot))
                {
                    Directory.Delete(this.testRoot, recursive: true);
                }
            }
            finally
            {
                this.environment.Dispose();
            }
        }

        private GVFSEnlistment CreateWorktreeEnlistment(string gitBinPath)
        {
            return GVFSEnlistment.CreateForWorktree(
                this.primaryRoot, gitBinPath, authentication: null,
                GVFSEnlistment.TryGetWorktreeInfo(this.worktreePath),
                repoUrl: "https://mock/repo");
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

        private string GetConfigValues(string configFile, string key)
        {
            ProcessResult result = GitProcess.InvokeProcess(this.worktreePath, $"config --file \"{configFile}\" --get-all {key}");
            (result.ExitCode == 0 || result.ExitCode == 1).ShouldBeTrue(result.Errors);
            return result.Output.Trim();
        }

        private void AppendToSharedConfig(string text)
        {
            File.AppendAllText(this.SharedConfigPath, text);
        }
    }
}
