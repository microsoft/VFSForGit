using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Service;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Windows.Mock.Upgrader;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.UnitTests.Windows.Windows.Service
{
    public class ProductUpgraderTimerTests
    {
        private string response_1 = @"{""Version"":""1.2.3.4""}";

        private interface IHttpMessageHandlerProtectedMembers
        {
            Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken token);
        }

        [TestCase]
        public void QueriesGitHubForUpgrade()
        {
            MockTracer tracer = new MockTracer();
            Mock<PhysicalFileSystem> fileSystemMock = new Mock<PhysicalFileSystem>();
            MockLocalGVFSConfig gvfsConfig = this.BuildGvfsConfig();

            Mock<InstallerRunPreCheckerBase> installerPreRunChecker = new Mock<InstallerRunPreCheckerBase>();

            Mock<HttpMessageHandler> handlerMock = new Mock<HttpMessageHandler>();

            handlerMock.Protected().As<IHttpMessageHandlerProtectedMembers>()
                .Setup(m => m.SendAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HttpResponseMessage()
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(this.response_1)
                })
                .Verifiable();

            HttpClient httpClient = new HttpClient(handlerMock.Object);

            string errorMessage = string.Empty;
            installerPreRunChecker.Setup(m => m.IsInstallationBlockedByRunningProcess(out errorMessage)).Returns(true);
            installerPreRunChecker.Setup(m => m.TryMountAllGVFSRepos(out errorMessage)).Returns(true);
            installerPreRunChecker.Setup(m => m.TryRunPreUpgradeChecks(out errorMessage)).Returns(true);
            installerPreRunChecker.Setup(m => m.TryUnmountAllGVFSRepos(out errorMessage)).Returns(true);

            using (ProductUpgradeTimer upgradeChecker = new ProductUpgradeTimer(tracer, fileSystemMock.Object, gvfsConfig, httpClient, installerPreRunChecker.Object))
            {
                upgradeChecker.TimerCallback(null);
            }
        }

        private MockLocalGVFSConfig BuildGvfsConfig()
        {
            MockLocalGVFSConfig gvfsConfig = new MockLocalGVFSConfig();
            gvfsConfig.TrySetConfig(GVFSConstants.LocalGVFSConfig.UpgradeRing, "slow", out _);
            gvfsConfig.TrySetConfig(GVFSConstants.LocalGVFSConfig.UpgradeFeedPackageName, "package", out _);
            gvfsConfig.TrySetConfig(GVFSConstants.LocalGVFSConfig.UpgradeFeedUrl, "https://pkgs.dev.azure.com/contoso/", out _);
            return gvfsConfig;
        }
    }
}
