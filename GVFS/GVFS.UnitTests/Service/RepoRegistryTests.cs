using GVFS.Common;
using GVFS.Service;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System;
using System.IO;
using System.Reflection;

namespace GVFS.UnitTests.Service
{
    [TestFixture]
    public class RepoRegistryTests
    {
        [TestCase]
        public void TryRegisterRepo_EmptyRegistry()
        {
            string dataLocation = Path.Combine(GetTestWorkingDir(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(dataLocation);
            RepoRegistry registry = new RepoRegistry(new MockTracer(), dataLocation);

            string repoRoot = "c:\test";
            string ownerSID = Guid.NewGuid().ToString();

            string errorMessage;
            registry.TryRegisterRepo(repoRoot, ownerSID, out errorMessage).ShouldEqual(true);

            var verifiableRegistry = registry.ReadRegistry();
            verifiableRegistry.Count.ShouldEqual(1);
            this.VerifyRepo(verifiableRegistry[repoRoot], ownerSID, expectedIsActive: true);
        }

        [TestCase]
        public void ReadRegistry_Upgrade_ExistingVersion1()
        {
            /*
             {"EnlistmentRoot":"c:\\code\\repo1","IsActive":false}
             {"EnlistmentRoot":"c:\\code\\repo2","IsActive":true}
            */

            string dataLocation = Path.Combine(GetTestWorkingDir(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(dataLocation);
            File.Copy(GetDataPath("Version1Registry"), Path.Combine(dataLocation, RepoRegistry.RegistryName));

            RepoRegistry registry = new RepoRegistry(new MockTracer(), dataLocation);
            registry.Upgrade();

            var repos = registry.ReadRegistry();
            repos.Count.ShouldEqual(2);

            this.VerifyRepo(repos["c:\\code\\repo1"], expectedOwnerSID: null, expectedIsActive: false);
            this.VerifyRepo(repos["c:\\code\\repo2"], expectedOwnerSID: null, expectedIsActive: true);
        }

        [TestCase]
        public void ReadRegistry_Upgrade_NoRegistry()
        {
            string dataLocation = Path.Combine(GetTestWorkingDir(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(dataLocation);

            RepoRegistry registry = new RepoRegistry(new MockTracer(), dataLocation);
            registry.Upgrade();

            var repos = registry.ReadRegistry();
            repos.Count.ShouldEqual(0);
        }

        private static string GetDataPath(string fileName)
        {
            string workingDirectory = GetTestWorkingDir();
            return Path.Combine(workingDirectory, "Data", fileName);
        }

        private static string GetTestWorkingDir()
        {
            return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        }

        private void VerifyRepo(RepoRegistration repo, string expectedOwnerSID, bool expectedIsActive)
        {
            repo.OwnerSID.ShouldEqual(expectedOwnerSID);
            repo.IsActive.ShouldEqual(expectedIsActive);
        }
    }
}
