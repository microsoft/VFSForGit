using GVFS.Service;
using GVFS.Service.Handlers;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.FileSystem;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GVFS.UnitTests.Service
{
    [TestFixture]
    public class RepoRegistryTests
    {
        private Mock<IRepoMounter> mockRepoMounter;
        private Mock<INotificationHandler> mockNotificationHandler;

         [SetUp]
        public void Setup()
        {
            this.mockRepoMounter = new Mock<IRepoMounter>(MockBehavior.Strict);
            this.mockNotificationHandler = new Mock<INotificationHandler>(MockBehavior.Strict);
        }

         [TearDown]
        public void TearDown()
        {
            this.mockRepoMounter.VerifyAll();
            this.mockNotificationHandler.VerifyAll();
        }

        [TestCase]
        public void TryRegisterRepo_EmptyRegistry()
        {
            string dataLocation = Path.Combine("mock:", "registryDataFolder");

            MockFileSystem fileSystem = new MockFileSystem(new MockDirectory(dataLocation, null, null));
            RepoRegistry registry = new RepoRegistry(
                new MockTracer(),
                fileSystem,
                dataLocation,
                this.mockRepoMounter.Object,
                this.mockNotificationHandler.Object);

            string repoRoot = Path.Combine("c:", "test");
            string ownerSID = Guid.NewGuid().ToString();

            string errorMessage;
            registry.TryRegisterRepo(repoRoot, ownerSID, out errorMessage).ShouldEqual(true);

            Dictionary<string, RepoRegistration> verifiableRegistry = registry.ReadRegistry();
            verifiableRegistry.Count.ShouldEqual(1);
            this.VerifyRepo(verifiableRegistry[repoRoot], ownerSID, expectedIsActive: true);
        }

        [TestCase]
        public void ReadRegistry_Upgrade_ExistingVersion1()
        {
            string dataLocation = Path.Combine("mock:", "registryDataFolder");
            MockFileSystem fileSystem = new MockFileSystem(new MockDirectory(dataLocation, null, null));

            string repo1 = Path.Combine("mock:", "code", "repo1");
            string repo2 = Path.Combine("mock:", "code", "repo2");

            // Create a version 1 registry file
            fileSystem.WriteAllText(
                Path.Combine(dataLocation, RepoRegistry.RegistryName),
$@"1
{{""EnlistmentRoot"":""{repo1.Replace("\\", "\\\\")}"",""IsActive"":false}}
{{""EnlistmentRoot"":""{repo2.Replace("\\", "\\\\")}"",""IsActive"":true}}
");

            RepoRegistry registry = new RepoRegistry(
                new MockTracer(),
                fileSystem,
                dataLocation,
                this.mockRepoMounter.Object,
                this.mockNotificationHandler.Object);
            registry.Upgrade();

            Dictionary<string, RepoRegistration> repos = registry.ReadRegistry();
            repos.Count.ShouldEqual(2);

            this.VerifyRepo(repos[repo1], expectedOwnerSID: null, expectedIsActive: false);
            this.VerifyRepo(repos[repo2], expectedOwnerSID: null, expectedIsActive: true);
        }

        [TestCase]
        public void ReadRegistry_Upgrade_NoRegistry()
        {
            string dataLocation = Path.Combine("mock:", "registryDataFolder");
            MockFileSystem fileSystem = new MockFileSystem(new MockDirectory(dataLocation, null, null));
            RepoRegistry registry = new RepoRegistry(
                new MockTracer(),
                fileSystem,
                dataLocation,
                this.mockRepoMounter.Object,
                this.mockNotificationHandler.Object);
            registry.Upgrade();

            Dictionary<string, RepoRegistration> repos = registry.ReadRegistry();
            repos.Count.ShouldEqual(0);
        }

        [TestCase]
        public void TryGetActiveRepos_BeforeAndAfterActivateAndDeactivate()
        {
            string dataLocation = Path.Combine("mock:", "registryDataFolder");
            MockFileSystem fileSystem = new MockFileSystem(new MockDirectory(dataLocation, null, null));
            RepoRegistry registry = new RepoRegistry(
                new MockTracer(),
                fileSystem,
                dataLocation,
                this.mockRepoMounter.Object,
                this.mockNotificationHandler.Object);

            string repo1Root = Path.Combine("mock:", "test", "repo1");
            string owner1SID = Guid.NewGuid().ToString();
            string repo2Root = Path.Combine("mock:", "test", "repo2");
            string owner2SID = Guid.NewGuid().ToString();
            string repo3Root = Path.Combine("mock:", "test", "repo3");
            string owner3SID = Guid.NewGuid().ToString();

            // Register all 3 repos
            string errorMessage;
            registry.TryRegisterRepo(repo1Root, owner1SID, out errorMessage).ShouldEqual(true);
            registry.TryRegisterRepo(repo2Root, owner2SID, out errorMessage).ShouldEqual(true);
            registry.TryRegisterRepo(repo3Root, owner3SID, out errorMessage).ShouldEqual(true);

            // Confirm all 3 active
            List<RepoRegistration> activeRepos;
            registry.TryGetActiveRepos(out activeRepos, out errorMessage);
            activeRepos.Count.ShouldEqual(3);
            this.VerifyRepo(activeRepos.SingleOrDefault(repo => repo.EnlistmentRoot.Equals(repo1Root)), owner1SID, expectedIsActive: true);
            this.VerifyRepo(activeRepos.SingleOrDefault(repo => repo.EnlistmentRoot.Equals(repo2Root)), owner2SID, expectedIsActive: true);
            this.VerifyRepo(activeRepos.SingleOrDefault(repo => repo.EnlistmentRoot.Equals(repo3Root)), owner3SID, expectedIsActive: true);

            // Deactive repo 2
            registry.TryDeactivateRepo(repo2Root, out errorMessage).ShouldEqual(true);

            // Confirm repos 1 and 3 still active
            registry.TryGetActiveRepos(out activeRepos, out errorMessage);
            activeRepos.Count.ShouldEqual(2);
            this.VerifyRepo(activeRepos.SingleOrDefault(repo => repo.EnlistmentRoot.Equals(repo1Root)), owner1SID, expectedIsActive: true);
            this.VerifyRepo(activeRepos.SingleOrDefault(repo => repo.EnlistmentRoot.Equals(repo3Root)), owner3SID, expectedIsActive: true);

            // Activate repo 2
            registry.TryRegisterRepo(repo2Root, owner2SID, out errorMessage).ShouldEqual(true);

            // Confirm all 3 active
            registry.TryGetActiveRepos(out activeRepos, out errorMessage);
            activeRepos.Count.ShouldEqual(3);
            this.VerifyRepo(activeRepos.SingleOrDefault(repo => repo.EnlistmentRoot.Equals(repo1Root)), owner1SID, expectedIsActive: true);
            this.VerifyRepo(activeRepos.SingleOrDefault(repo => repo.EnlistmentRoot.Equals(repo2Root)), owner2SID, expectedIsActive: true);
            this.VerifyRepo(activeRepos.SingleOrDefault(repo => repo.EnlistmentRoot.Equals(repo3Root)), owner3SID, expectedIsActive: true);
        }

        [TestCase]
        public void TryDeactivateRepo()
        {
            string dataLocation = Path.Combine("mock:", "registryDataFolder");
            MockFileSystem fileSystem = new MockFileSystem(new MockDirectory(dataLocation, null, null));
            RepoRegistry registry = new RepoRegistry(
                new MockTracer(),
                fileSystem,
                dataLocation,
                this.mockRepoMounter.Object,
                this.mockNotificationHandler.Object);

            string repo1Root = Path.Combine("mock:", "test", "repo1");
            string owner1SID = Guid.NewGuid().ToString();
            string errorMessage;
            registry.TryRegisterRepo(repo1Root, owner1SID, out errorMessage).ShouldEqual(true);

            List<RepoRegistration> activeRepos;
            registry.TryGetActiveRepos(out activeRepos, out errorMessage);
            activeRepos.Count.ShouldEqual(1);
            this.VerifyRepo(activeRepos.SingleOrDefault(repo => repo.EnlistmentRoot.Equals(repo1Root)), owner1SID, expectedIsActive: true);

            // Deactivate repo
            registry.TryDeactivateRepo(repo1Root, out errorMessage).ShouldEqual(true);
            registry.TryGetActiveRepos(out activeRepos, out errorMessage);
            activeRepos.Count.ShouldEqual(0);

            // Deactivate repo again (no-op)
            registry.TryDeactivateRepo(repo1Root, out errorMessage).ShouldEqual(true);
            registry.TryGetActiveRepos(out activeRepos, out errorMessage);
            activeRepos.Count.ShouldEqual(0);

            // Repo should still be in the registry
            Dictionary<string, RepoRegistration> verifiableRegistry = registry.ReadRegistry();
            verifiableRegistry.Count.ShouldEqual(1);
            this.VerifyRepo(verifiableRegistry[repo1Root], owner1SID, expectedIsActive: false);

            // Deactivate non-existent repo should fail
            string nonExistantPath = Path.Combine("mock:", "test", "doesNotExist");
            registry.TryDeactivateRepo(nonExistantPath, out errorMessage).ShouldEqual(false);
            errorMessage.ShouldContain("Attempted to deactivate non-existent repo");
        }

        [TestCase]
        public void TraceStatus()
        {
            string dataLocation = Path.Combine("mock:", "registryDataFolder");
            MockFileSystem fileSystem = new MockFileSystem(new MockDirectory(dataLocation, null, null));
            MockTracer tracer = new MockTracer();
            RepoRegistry registry = new RepoRegistry(
                tracer,
                fileSystem,
                dataLocation,
                this.mockRepoMounter.Object,
                this.mockNotificationHandler.Object);

            string repo1Root = Path.Combine("mock:", "test", "repo1");
            string owner1SID = Guid.NewGuid().ToString();
            string repo2Root = Path.Combine("mock:", "test", "repo2");
            string owner2SID = Guid.NewGuid().ToString();
            string repo3Root = Path.Combine("mock:", "test", "repo3");
            string owner3SID = Guid.NewGuid().ToString();

            string errorMessage;
            registry.TryRegisterRepo(repo1Root, owner1SID, out errorMessage).ShouldEqual(true);
            registry.TryRegisterRepo(repo2Root, owner2SID, out errorMessage).ShouldEqual(true);
            registry.TryRegisterRepo(repo3Root, owner3SID, out errorMessage).ShouldEqual(true);
            registry.TryDeactivateRepo(repo2Root, out errorMessage).ShouldEqual(true);

            registry.TraceStatus();

            Dictionary<string, RepoRegistration> repos = registry.ReadRegistry();
            repos.Count.ShouldEqual(3);
            foreach (KeyValuePair<string, RepoRegistration> kvp in repos)
            {
                tracer.RelatedInfoEvents.SingleOrDefault(message => message.Equals(kvp.Value.ToString())).ShouldNotBeNull();
            }
        }

        private void VerifyRepo(RepoRegistration repo, string expectedOwnerSID, bool expectedIsActive)
        {
            repo.ShouldNotBeNull();
            repo.OwnerSID.ShouldEqual(expectedOwnerSID);
            repo.IsActive.ShouldEqual(expectedIsActive);
        }
    }
}
