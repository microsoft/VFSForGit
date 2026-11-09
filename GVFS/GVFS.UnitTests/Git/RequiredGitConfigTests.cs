using GVFS.Common.Git;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.Git;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace GVFS.UnitTests.Git
{
    [TestFixture]
    public class RequiredGitConfigTests
    {
        private const string ListLocalConfigCommand = "config --list --local -z";
        private const string SetCommandPrefix = "config --local --replace-all";
        private const string ConfigLockError = "error: could not lock config file .git/config: File exists";
        private const int ConfigLockFailureCode = 255;

        [TestCase]
        public void AcceptsFailedSharedWriteWhenTheValueIsPresent()
        {
            MockGitProcess git = CreateGitProcess(string.Empty, "core.autocrlf\nfalse\0");
            git.SetExpectedCommandResult(SetCommandPrefix, () => new GitProcess.Result(string.Empty, ConfigLockError, ConfigLockFailureCode), matchPrefix: true);

            string error;
            RequiredGitConfig.TrySetRequiredSettings(new MockGVFSEnlistment(git), CreateAutoCrlfSettings(), out error).ShouldBeTrue(error);

            CountSetCommands(git).ShouldEqual(1);
        }

        [TestCase]
        public void RetriesFailedSharedWriteUntilItSucceeds()
        {
            int setAttempts = 0;
            MockGitProcess git = CreateGitProcess(string.Empty);
            git.SetExpectedCommandResult(
                SetCommandPrefix,
                () => ++setAttempts == 1
                    ? new GitProcess.Result(string.Empty, ConfigLockError, ConfigLockFailureCode)
                    : new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.SuccessCode),
                matchPrefix: true);

            string error;
            RequiredGitConfig.TrySetRequiredSettings(new MockGVFSEnlistment(git), CreateAutoCrlfSettings(), out error).ShouldBeTrue(error);

            CountSetCommands(git).ShouldEqual(2);
        }

        [TestCase]
        public void FailsWhenSharedWriteFailsAndTheValueStaysMissing()
        {
            MockGitProcess git = CreateGitProcess(string.Empty);
            git.SetExpectedCommandResult(SetCommandPrefix, () => new GitProcess.Result(string.Empty, ConfigLockError, ConfigLockFailureCode), matchPrefix: true);

            string error;
            RequiredGitConfig.TrySetRequiredSettings(new MockGVFSEnlistment(git), CreateAutoCrlfSettings(), out error).ShouldBeFalse();

            error.ShouldContain("core.autocrlf", "could not lock config file");
            CountSetCommands(git).ShouldEqual(RequiredGitConfig.SharedWriteAttempts);
        }

        [TestCase]
        public void RetriesFailedSharedWriteWhenAnotherValueIsAlsoPresent()
        {
            MockGitProcess git = CreateGitProcess(string.Empty, "core.autocrlf\nfalse\0core.autocrlf\ntrue\0");
            git.SetExpectedCommandResult(SetCommandPrefix, () => new GitProcess.Result(string.Empty, ConfigLockError, ConfigLockFailureCode), matchPrefix: true);

            string error;
            RequiredGitConfig.TrySetRequiredSettings(new MockGVFSEnlistment(git), CreateAutoCrlfSettings(), out error).ShouldBeFalse();

            CountSetCommands(git).ShouldEqual(RequiredGitConfig.SharedWriteAttempts);
        }

        [TestCase]
        public void DoesNotRewriteAPresentSettingWithQuotedSubsection()
        {
            MockGitProcess git = CreateGitProcess("credential.https://dev.azure.com.usehttppath\ntrue\0");

            string error;
            RequiredGitConfig.TrySetRequiredSettings(
                new MockGVFSEnlistment(git),
                new Dictionary<string, string> { { "credential.\"https://dev.azure.com\".useHttpPath", "true" } },
                out error).ShouldBeTrue(error);

            git.CommandsRun.Single().ShouldEqual(ListLocalConfigCommand);
        }

        [TestCase]
        public void RewritesASettingWithSeveralValues()
        {
            MockGitProcess git = CreateGitProcess("core.autocrlf\nfalse\0core.autocrlf\ntrue\0");
            git.SetExpectedCommandResult(SetCommandPrefix, () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.SuccessCode), matchPrefix: true);

            string error;
            RequiredGitConfig.TrySetRequiredSettings(new MockGVFSEnlistment(git), CreateAutoCrlfSettings(), out error).ShouldBeTrue(error);

            CountSetCommands(git).ShouldEqual(1);
        }

        [TestCase]
        public void RewritesASettingWithoutAValue()
        {
            MockGitProcess git = CreateGitProcess("core.autocrlf\0");
            git.SetExpectedCommandResult(SetCommandPrefix, () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.SuccessCode), matchPrefix: true);

            string error;
            RequiredGitConfig.TrySetRequiredSettings(new MockGVFSEnlistment(git), CreateAutoCrlfSettings(), out error).ShouldBeTrue(error);

            CountSetCommands(git).ShouldEqual(1);
        }

        [TestCase]
        public void FailsWhenTheLocalConfigCannotBeRead()
        {
            MockGitProcess git = new MockGitProcess();
            git.SetExpectedCommandResult(ListLocalConfigCommand, () => new GitProcess.Result(string.Empty, "fatal: bad config line 1 in file .git/config", 128));

            string error;
            RequiredGitConfig.TrySetRequiredSettings(new MockGVFSEnlistment(git), CreateAutoCrlfSettings(), out error).ShouldBeFalse();

            error.ShouldEqual("Could not read the local git config");
        }

        /// <summary>
        /// Returns a mock whose local config list gives <paramref name="listOutputs"/> in order,
        /// and then the last output again.
        /// </summary>
        private static MockGitProcess CreateGitProcess(params string[] listOutputs)
        {
            int listCount = 0;
            MockGitProcess git = new MockGitProcess();
            git.SetExpectedCommandResult(
                ListLocalConfigCommand,
                () => new GitProcess.Result(listOutputs[System.Math.Min(listCount++, listOutputs.Length - 1)], string.Empty, GitProcess.Result.SuccessCode));
            return git;
        }

        private static Dictionary<string, string> CreateAutoCrlfSettings()
        {
            return new Dictionary<string, string> { { "core.autocrlf", "false" } };
        }

        private static int CountSetCommands(MockGitProcess git)
        {
            return git.CommandsRun.Count(command => command.StartsWith(SetCommandPrefix) && command.Contains("\"core.autocrlf\" \"false\""));
        }
    }
}
