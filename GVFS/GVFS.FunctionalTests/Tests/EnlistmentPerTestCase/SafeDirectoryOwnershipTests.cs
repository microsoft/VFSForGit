using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using GVFS.FunctionalTests.Tools;
using GVFS.UnitTests.Category;
using NUnit.Framework;
using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerTestCase
{
    [TestFixture]
    /* Not inheriting from TestsWithEnlistmentPerTestCase because we don't need to mount
     * the repo for this test. */
    public class SafeDirectoryOwnershipTests
    {
        private GVFSEnlistment Enlistment;
        private static readonly SecurityIdentifier usersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);

        [SetUp]
        public void SetUp()
        {
            var enlistmentRoot = GVFSFunctionalTestEnlistment.GetUniqueEnlistmentRoot();
            Enlistment = new GVFSEnlistment(
                enlistmentRoot,
                GVFSTestConfig.RepoToClone,
                GVFSPlatform.Instance.GitInstallation.GetInstalledGitBinPath(),
                authentication: null);
            var process = Enlistment.CreateGitProcess();
            Common.Git.GitProcess.Init(Enlistment);
        }

        [TestCase]
        public void RepoOpensIfSafeDirectoryConfigIsSet()
        {
            var repoDir = this.Enlistment.WorkingDirectoryBackingRoot;
            using (var safeDirectoryConfig = WithSafeDirectoryConfig(repoDir))
            using (var enlistmentOwner = WithEnlistmentOwner(usersSid))
            using (LibGit2Repo repo = new LibGit2Repo(NullTracer.Instance, repoDir))
            {
                // repo is opened in the constructor
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        [Category(CategoryConstants.CaseInsensitiveFileSystemOnly)]
        public void RepoOpensEvenIfSafeDirectoryConfigIsCaseMismatched(bool upperCase)
        {
            var repoDir = this.Enlistment.WorkingDirectoryBackingRoot;

            if (upperCase)
            {
                repoDir = repoDir.ToUpperInvariant();
            }
            else
            {
                repoDir = repoDir.ToLowerInvariant();
            }
            using (var safeDirectoryConfig = WithSafeDirectoryConfig(this.Enlistment.WorkingDirectoryBackingRoot))
            using (var enlistmentOwner = WithEnlistmentOwner(usersSid))
            using (LibGit2Repo repo = new LibGit2Repo(NullTracer.Instance, repoDir))
            {
                // repo is opened in the constructor
            }
        }

        private class Disposable : IDisposable
        {
            private readonly Action onDispose;

            public Disposable(Action onDispose)
            {
                this.onDispose = onDispose;
            }

            public void Dispose()
            {
                onDispose();
            }
        }

        private IDisposable WithSafeDirectoryConfig(string repoDir)
        {
            Tools.GitProcess.Invoke(null, $"config --global --add safe.directory \"{repoDir}\"");
            return new Disposable(() =>
                Tools.GitProcess.Invoke(null, $"config --global --unset safe.directory \"{repoDir}\""));
        }

        private IDisposable WithEnlistmentOwner(SecurityIdentifier newOwner)
        {
            var repoDir = this.Enlistment.WorkingDirectoryBackingRoot;
            var currentOwner = GetDirectoryOwner(repoDir);

            SetDirectoryOwner(repoDir, newOwner);
            var updatedOwner = GetDirectoryOwner(repoDir);
            return new Disposable(() =>
                SetDirectoryOwner(repoDir, currentOwner));
        }

        private SecurityIdentifier GetDirectoryOwner(string directory)
        {
            DirectorySecurity repoSecurity = Directory.GetAccessControl(directory);
            return (SecurityIdentifier)repoSecurity.GetOwner(typeof(SecurityIdentifier));
        }

        private void SetDirectoryOwner(string directory, SecurityIdentifier newOwner)
        {
            using (new PrivilegeEnabler(PrivilegeEnabler.AllowChangeOwnerToGroup))
            {
                DirectorySecurity repoSecurity = Directory.GetAccessControl(directory);
                repoSecurity.SetOwner(newOwner);
                Directory.SetAccessControl(directory, repoSecurity);
            }
        }
    }
}
