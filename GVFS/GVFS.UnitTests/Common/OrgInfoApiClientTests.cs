using GVFS.Common;
using GVFS.Tests.Should;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class OrgInfoServerTests
    {
        public static List<OrgInfo> TestOrgInfo = new List<OrgInfo>()
        {
            new OrgInfo() { OrgName = "org1", Platform = "windows", Ring = "fast", Version = "1.2.3.1" },
            new OrgInfo() { OrgName = "org1", Platform = "windows", Ring = "slow", Version = "1.2.3.2" },
            new OrgInfo() { OrgName = "org1", Platform = "macOS", Ring = "fast", Version = "1.2.3.3" },
            new OrgInfo() { OrgName = "org1", Platform = "macOS", Ring = "slow", Version = "1.2.3.4" },
            new OrgInfo() { OrgName = "org2", Platform = "windows", Ring = "fast", Version = "1.2.3.5" },
            new OrgInfo() { OrgName = "org2", Platform = "windows", Ring = "slow", Version = "1.2.3.6" },
            new OrgInfo() { OrgName = "org2", Platform = "macOS", Ring = "fast", Version = "1.2.3.7" },
            new OrgInfo() { OrgName = "org2", Platform = "macOS", Ring = "slow", Version = "1.2.3.8" },
        };

        private string baseUrl = "https://www.contoso.com";

        private interface IHttpMessageHandlerProtectedMembers
        {
            Task<HttpResponseMessage>  SendAsync(HttpRequestMessage message, CancellationToken token);
        }

        [TestCaseSource("TestOrgInfo")]
        public void QueryNewestVersionWithParams(OrgInfo orgInfo)
        {
            Mock<HttpMessageHandler> handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            handlerMock.Protected().As<IHttpMessageHandlerProtectedMembers>()
                .Setup(m => m.SendAsync(It.Is<HttpRequestMessage>(request => this.UriMatches(request.RequestUri, this.baseUrl, orgInfo.OrgName, orgInfo.Platform, orgInfo.Ring)), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HttpResponseMessage()
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(this.ConstructResponseContent(orgInfo.Version))
                });

            HttpClient httpClient = new HttpClient(handlerMock.Object);

            OrgInfoApiClient upgradeChecker = new OrgInfoApiClient(httpClient, this.baseUrl);
            Version version = upgradeChecker.QueryNewestVersion(orgInfo.OrgName, orgInfo.Platform, orgInfo.Ring);

            version.ShouldEqual(new Version(orgInfo.Version));

            handlerMock.VerifyAll();
        }

        private bool UriMatches(Uri uri, string baseUrl, string expectedOrgName, string expectedPlatform, string expectedRing)
        {
            bool hostMatches = uri.Host.Equals(baseUrl);

            Dictionary<string, string> queryParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string param in uri.Query.Substring(1).Split('&'))
            {
                string[] fields = param.Split('=');
                string key = fields[0];
                string value = fields[1];

                queryParams.Add(key, value);
            }

            if (queryParams.Count != 3)
            {
                return false;
            }

            if (!queryParams.TryGetValue("Organization", out string orgName) || !string.Equals(orgName, expectedOrgName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!queryParams.TryGetValue("platform", out string platform) || !string.Equals(platform, expectedPlatform, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!queryParams.TryGetValue("ring", out string ring) || !string.Equals(ring, expectedRing, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        private string ConstructResponseContent(string version)
        {
            return $"{{\"version\" : \"{version}\"}} ";
        }

        public class OrgInfo
        {
            public string OrgName { get; set; }
            public string Ring { get; set; }
            public string Platform { get; set; }
            public string Version { get; set; }
        }
    }
}
