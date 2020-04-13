using GVFS.Common;
using GVFS.Common.NamedPipes;
using GVFS.Service;
using GVFS.Service.Handlers;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.FileSystem;
using Moq;
using NUnit.Framework;
using System.IO;

namespace GVFS.UnitTests.Service.Mac
{
    [TestFixture]
    public class MacServiceTests
    {
        private const string GVFSServiceName = "GVFS.Service";
        private const int ExpectedActiveUserId = 502;
        private const int ExpectedSessionId = 502;
        private static readonly string ExpectedActiveRepoPath = Path.Combine("mock:", "code", "repo2");
        private static readonly string ServiceDataLocation = Path.Combine("mock:", "registryDataFolder");

        private MockFileSystem fileSystem;
        private MockTracer tracer;
        private MockPlatform gvfsPlatform;

        [SetUp]
        public void SetUp()
        {
            this.tracer = new MockTracer();
            this.fileSystem = new MockFileSystem(new MockDirectory(ServiceDataLocation, null, null));
            this.gvfsPlatform = (MockPlatform)GVFSPlatform.Instance;
            this.gvfsPlatform.MockCurrentUser = ExpectedActiveUserId.ToString();
        }

        [TestCase]
        public void ServiceStartTriggersAutoMountForCurrentUser()
        {
            Mock<IRepoRegistry> repoRegistry = new Mock<IRepoRegistry>(MockBehavior.Strict);
            repoRegistry.Setup(r => r.AutoMountRepos(ExpectedActiveUserId.ToString(), ExpectedSessionId, true));
            repoRegistry.Setup(r => r.TraceStatus());

            GVFSService service = new GVFSService(
                this.tracer,
                serviceName: null,
                repoRegistry: repoRegistry.Object);

            service.Run();

            repoRegistry.VerifyAll();
        }

        [TestCase]
        public void ServiceHandlesEnablePrjfsRequest()
        {
            string expectedServiceResponse = "TRequestResponse|{\"State\":1,\"ErrorMessage\":null}";
            Mock<NamedPipeServer.Connection> connectionMock = new Mock<NamedPipeServer.Connection>(
                MockBehavior.Strict,
                null, // serverStream
                this.tracer, // tracer
                null); // isStopping
            connectionMock.Setup(mp => mp.TrySendResponse(expectedServiceResponse)).Returns(true);

            NamedPipeMessages.EnableAndAttachProjFSRequest request = new NamedPipeMessages.EnableAndAttachProjFSRequest();
            request.EnlistmentRoot = string.Empty;

            RequestHandler serviceRequestHandler = new RequestHandler(this.tracer, etwArea: string.Empty, repoRegistry: null);
            serviceRequestHandler.HandleRequest(this.tracer, request.ToMessage().ToString(), connectionMock.Object);

            connectionMock.VerifyAll();
        }

        [TestCase]
        public void RepoRegistryMountsOnlyRegisteredRepos()
        {
            Mock<IRepoMounter> repoMounterMock = new Mock<IRepoMounter>(MockBehavior.Strict);
            repoMounterMock.Setup(mp => mp.MountRepository(ExpectedActiveRepoPath, ExpectedActiveUserId)).Returns(true);

            this.CreateTestRepos(ServiceDataLocation);

            RepoRegistry repoRegistry = new RepoRegistry(
                this.tracer,
                this.fileSystem,
                ServiceDataLocation,
                repoMounterMock.Object,
                null);

            repoRegistry.AutoMountRepos(ExpectedActiveUserId.ToString(), ExpectedSessionId, checkDirectoryExists: false);

            repoMounterMock.VerifyAll();
        }

        [TestCase]
        public void MountProcessLaunchedUsingCorrectArgs()
        {
            string executable = @"/bin/launchctl";
            string gvfsBinPath = Path.Combine(this.gvfsPlatform.Constants.GVFSBinDirectoryPath, this.gvfsPlatform.Constants.GVFSExecutableName);
            string expectedArgs = $"asuser {ExpectedActiveUserId} {gvfsBinPath} mount {ExpectedActiveRepoPath}";

            Mock<GVFSMountProcess.MountLauncher> mountLauncherMock = new Mock<GVFSMountProcess.MountLauncher>(MockBehavior.Strict, this.tracer);
            mountLauncherMock.Setup(mp => mp.LaunchProcess(
                executable,
                expectedArgs,
                ExpectedActiveRepoPath))
                .Returns(true);

            string errorString = null;
            mountLauncherMock.Setup(mp => mp.WaitUntilMounted(
                this.tracer,
                ExpectedActiveRepoPath,
                It.IsAny<bool>(),
                out errorString))
                .Returns(true);

            GVFSMountProcess mountProcess = new GVFSMountProcess(this.tracer, mountLauncherMock.Object);
            mountProcess.MountRepository(ExpectedActiveRepoPath, ExpectedActiveUserId);

            mountLauncherMock.VerifyAll();
        }

        private void CreateTestRepos(string dataLocation)
        {
            string repo1 = Path.Combine("mock:", "code", "repo1");
            string repo2 = ExpectedActiveRepoPath;
            string repo3 = Path.Combine("mock:", "code", "repo3");
            string repo4 = Path.Combine("mock:", "code", "repo4");

            this.fileSystem.WriteAllText(
                Path.Combine(dataLocation, RepoRegistry.RegistryName),
                $@"1
                {{""EnlistmentRoot"":""{repo1.Replace("\\", "\\\\")}"",""OwnerSID"":502,""IsActive"":false}}
                {{""EnlistmentRoot"":""{repo2.Replace("\\", "\\\\")}"",""OwnerSID"":502,""IsActive"":true}}
                {{""EnlistmentRoot"":""{repo3.Replace("\\", "\\\\")}"",""OwnerSID"":501,""IsActive"":false}}
                {{""EnlistmentRoot"":""{repo4.Replace("\\", "\\\\")}"",""OwnerSID"":501,""IsActive"":true}}
                ");
        }
    }
}
