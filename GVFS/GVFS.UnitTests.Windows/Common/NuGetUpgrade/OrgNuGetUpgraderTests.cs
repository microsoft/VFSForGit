using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.NuGetUpgrade;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.FileSystem;
using Moq;
using Moq.Protected;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.UnitTests.Common.NuGetUpgrade
{
    [TestFixture]
    public class OrgNuGetUpgraderTests
    {
        private const string CurrentVersion = "1.5.1185.0";
        private const string NewerVersion = "1.6.1185.0";

        private const string DefaultUpgradeFeedPackageName = "package";
        private const string DefaultUpgradeFeedUrl = "https://pkgs.dev.azure.com/contoso/";
        private const string DefaultOrgInfoServerUrl = "https://www.contoso.com";
        private const string DefaultRing = "slow";

        private OrgNuGetUpgrader upgrader;

        private MockTracer tracer;

        private OrgNuGetUpgrader.OrgNuGetUpgraderConfig upgraderConfig;

        private Mock<NuGetFeed> mockNuGetFeed;
        private MockFileSystem mockFileSystem;
        private Mock<ICredentialStore> mockCredentialManager;
        private Mock<HttpMessageHandler> httpMessageHandlerMock;

        private string downloadDirectoryPath = Path.Combine(
            $"mock:{Path.DirectorySeparatorChar}",
            ProductUpgraderInfo.UpgradeDirectoryName,
            ProductUpgraderInfo.DownloadDirectory);

        private interface IHttpMessageHandlerProtectedMembers
        {
            Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken token);
        }

        public static IEnumerable<Exception> NetworkFailureCases()
        {
            yield return new HttpRequestException("Response status code does not indicate success: 401: (Unauthorized)");
            yield return new TaskCanceledException("Task canceled");
        }

        [SetUp]
        public void SetUp()
        {
            MockLocalGVFSConfig mockGvfsConfig = new MockLocalGVFSConfigBuilder(
                DefaultRing,
                DefaultUpgradeFeedUrl,
                DefaultUpgradeFeedPackageName,
                DefaultOrgInfoServerUrl)
                .WithUpgradeRing()
                .WithUpgradeFeedPackageName()
                .WithUpgradeFeedUrl()
                .WithOrgInfoServerUrl()
                .Build();

            this.upgraderConfig = new OrgNuGetUpgrader.OrgNuGetUpgraderConfig(this.tracer, mockGvfsConfig);
            this.upgraderConfig.TryLoad(out _);

            this.tracer = new MockTracer();

            this.mockNuGetFeed = new Mock<NuGetFeed>(
                DefaultUpgradeFeedUrl,
                DefaultUpgradeFeedPackageName,
                this.downloadDirectoryPath,
                null,
                GVFSPlatform.Instance.UnderConstruction.SupportsNuGetEncryption,
                this.tracer);

            this.mockFileSystem = new MockFileSystem(
                new MockDirectory(
                    Path.GetDirectoryName(this.downloadDirectoryPath),
                    new[] { new MockDirectory(this.downloadDirectoryPath, null, null) },
                    null));

            this.mockCredentialManager = new Mock<ICredentialStore>();
            string credentialManagerString = "value";
            string emptyString = string.Empty;
            this.mockCredentialManager.Setup(foo => foo.TryGetCredential(It.IsAny<ITracer>(), It.IsAny<string>(), out credentialManagerString, out credentialManagerString, out credentialManagerString)).Returns(true);

            this.httpMessageHandlerMock = new Mock<HttpMessageHandler>();

            this.httpMessageHandlerMock.Protected().As<IHttpMessageHandlerProtectedMembers>()
                .Setup(m => m.SendAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HttpResponseMessage()
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(this.ConstructResponseContent(NewerVersion))
                });

            HttpClient httpClient = new HttpClient(this.httpMessageHandlerMock.Object);

            this.upgrader = new OrgNuGetUpgrader(
                CurrentVersion,
                this.tracer,
                this.mockFileSystem,
                httpClient,
                false,
                false,
                this.upgraderConfig,
                "windows",
                this.mockNuGetFeed.Object,
                this.mockCredentialManager.Object);
        }

        [TestCase]
        public void SupportsAnonymousQuery()
        {
            this.upgrader.SupportsAnonymousVersionQuery.ShouldBeTrue();
        }

        [TestCase]
        public void TryQueryNewestVersion()
        {
            Version newVersion;
            string message;

            bool success = this.upgrader.TryQueryNewestVersion(out newVersion, out message);

            success.ShouldBeTrue();
            newVersion.ShouldNotBeNull();
            newVersion.ShouldEqual<Version>(new Version(NewerVersion));
            message.ShouldNotBeNull();
            message.ShouldEqual($"New version {OrgNuGetUpgraderTests.NewerVersion} is available.");
        }

        [TestCaseSource("NetworkFailureCases")]
        public void HandlesNetworkErrors(Exception ex)
        {
            Version newVersion;
            string message;

            this.httpMessageHandlerMock.Protected().As<IHttpMessageHandlerProtectedMembers>()
                .Setup(m => m.SendAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(ex);

            bool success = this.upgrader.TryQueryNewestVersion(out newVersion, out message);

            success.ShouldBeFalse();
            newVersion.ShouldBeNull();
            message.ShouldNotBeNull();
            message.ShouldContain("Network error");
        }

        [TestCase]
        public void HandlesEmptyVersion()
        {
            Version newVersion;
            string message;

            this.httpMessageHandlerMock.Protected().As<IHttpMessageHandlerProtectedMembers>()
           .Setup(m => m.SendAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(new HttpResponseMessage()
           {
               StatusCode = HttpStatusCode.OK,
               Content = new StringContent(this.ConstructResponseContent(string.Empty))
           });

            bool success = this.upgrader.TryQueryNewestVersion(out newVersion, out message);

            success.ShouldBeTrue();
            newVersion.ShouldBeNull();
            message.ShouldNotBeNull();
            message.ShouldContain("No versions available");
        }

        private string ConstructResponseContent(string version)
        {
            return $"{{\"version\" : \"{version}\"}} ";
        }
    }
}
