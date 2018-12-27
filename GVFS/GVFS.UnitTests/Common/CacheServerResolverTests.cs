using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.Git;
using Newtonsoft.Json;
using NUnit.Framework;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class CacheServerResolverTests
    {
        private const string CacheServerUrl = "https://cache/server";
        private const string CacheServerName = "TestCacheServer";

        [TestCase]
        public void CanGetCacheServerFromNewConfig()
        {
            MockGVFSEnlistment enlistment = this.CreateEnlistment(CacheServerUrl);
            CacheServerInfo cacheServer = CacheServerResolver.GetCacheServerFromConfig(enlistment);

            cacheServer.Url.ShouldEqual(CacheServerUrl);
            CacheServerResolver.GetUrlFromConfig(enlistment).ShouldEqual(CacheServerUrl);
        }

        [TestCase]
        public void CanGetCacheServerFromOldConfig()
        {
            MockGVFSEnlistment enlistment = this.CreateEnlistment(null, CacheServerUrl);
            CacheServerInfo cacheServer = CacheServerResolver.GetCacheServerFromConfig(enlistment);

            cacheServer.Url.ShouldEqual(CacheServerUrl);
            CacheServerResolver.GetUrlFromConfig(enlistment).ShouldEqual(CacheServerUrl);
        }

        [TestCase]
        public void CanGetCacheServerWithNoConfig()
        {
            MockGVFSEnlistment enlistment = this.CreateEnlistment();

            this.ValidateIsNone(enlistment, CacheServerResolver.GetCacheServerFromConfig(enlistment));
            CacheServerResolver.GetUrlFromConfig(enlistment).ShouldEqual(enlistment.RepoUrl);
        }

        [TestCase]
        public void CanResolveUrlForKnownName()
        {
            CacheServerResolver resolver = this.CreateResolver();

            CacheServerInfo resolvedCacheServer;
            string error;
            resolver.TryResolveUrlFromRemote(CacheServerName, this.CreateGVFSConfig(), out resolvedCacheServer, out error);

            resolvedCacheServer.Url.ShouldEqual(CacheServerUrl);
            resolvedCacheServer.Name.ShouldEqual(CacheServerName);
        }

        [TestCase]
        public void CanResolveNameFromKnownUrl()
        {
            CacheServerResolver resolver = this.CreateResolver();
            CacheServerInfo resolvedCacheServer = resolver.ResolveNameFromRemote(CacheServerUrl, this.CreateGVFSConfig());

            resolvedCacheServer.Url.ShouldEqual(CacheServerUrl);
            resolvedCacheServer.Name.ShouldEqual(CacheServerName);
        }

        [TestCase]
        public void CanResolveNameFromCustomUrl()
        {
            const string CustomUrl = "https://not/a/known/cache/server";

            CacheServerResolver resolver = this.CreateResolver();
            CacheServerInfo resolvedCacheServer = resolver.ResolveNameFromRemote(CustomUrl, this.CreateGVFSConfig());

            resolvedCacheServer.Url.ShouldEqual(CustomUrl);
            resolvedCacheServer.Name.ShouldEqual(CacheServerInfo.ReservedNames.UserDefined);
        }

        [TestCase]
        public void CanResolveUrlAsRepoUrl()
        {
            MockGVFSEnlistment enlistment = this.CreateEnlistment();
            CacheServerResolver resolver = this.CreateResolver(enlistment);

            this.ValidateIsNone(enlistment, resolver.ResolveNameFromRemote(enlistment.RepoUrl, this.CreateGVFSConfig()));
            this.ValidateIsNone(enlistment, resolver.ResolveNameFromRemote(enlistment.RepoUrl + "/", this.CreateGVFSConfig()));
            this.ValidateIsNone(enlistment, resolver.ResolveNameFromRemote(enlistment.RepoUrl + "//", this.CreateGVFSConfig()));
            this.ValidateIsNone(enlistment, resolver.ResolveNameFromRemote(enlistment.RepoUrl.ToUpper(), this.CreateGVFSConfig()));
            this.ValidateIsNone(enlistment, resolver.ResolveNameFromRemote(enlistment.RepoUrl.ToUpper() + "/", this.CreateGVFSConfig()));
            this.ValidateIsNone(enlistment, resolver.ResolveNameFromRemote(enlistment.RepoUrl.ToLower(), this.CreateGVFSConfig()));
            this.ValidateIsNone(enlistment, resolver.ResolveNameFromRemote(enlistment.RepoUrl.ToLower() + "/", this.CreateGVFSConfig()));
        }

        [TestCase]
        public void CanParseUrl()
        {
            CacheServerResolver resolver = new CacheServerResolver(new MockTracer(), this.CreateEnlistment());
            CacheServerInfo parsedCacheServer = resolver.ParseUrlOrFriendlyName(CacheServerUrl);

            parsedCacheServer.Url.ShouldEqual(CacheServerUrl);
            parsedCacheServer.Name.ShouldEqual(CacheServerInfo.ReservedNames.UserDefined);
        }

        [TestCase]
        public void CanParseName()
        {
            CacheServerResolver resolver = new CacheServerResolver(new MockTracer(), this.CreateEnlistment());
            CacheServerInfo parsedCacheServer = resolver.ParseUrlOrFriendlyName(CacheServerName);

            parsedCacheServer.Url.ShouldEqual(null);
            parsedCacheServer.Name.ShouldEqual(CacheServerName);
        }

        [TestCase]
        public void CanParseAndResolveDefault()
        {
            CacheServerResolver resolver = this.CreateResolver();

            CacheServerInfo parsedCacheServer = resolver.ParseUrlOrFriendlyName(null);
            parsedCacheServer.Url.ShouldEqual(null);
            parsedCacheServer.Name.ShouldEqual(CacheServerInfo.ReservedNames.Default);

            CacheServerInfo resolvedCacheServer;
            string error;
            resolver.TryResolveUrlFromRemote(parsedCacheServer.Name, this.CreateGVFSConfig(), out resolvedCacheServer, out error);

            resolvedCacheServer.Url.ShouldEqual(CacheServerUrl);
            resolvedCacheServer.Name.ShouldEqual(CacheServerName);
        }

        [TestCase]
        public void CanParseAndResolveNoCacheServer()
        {
            MockGVFSEnlistment enlistment = this.CreateEnlistment();
            CacheServerResolver resolver = this.CreateResolver(enlistment);

            this.ValidateIsNone(enlistment, resolver.ParseUrlOrFriendlyName(CacheServerInfo.ReservedNames.None));
            this.ValidateIsNone(enlistment, resolver.ParseUrlOrFriendlyName(enlistment.RepoUrl));
            this.ValidateIsNone(enlistment, resolver.ParseUrlOrFriendlyName(enlistment.RepoUrl));
            this.ValidateIsNone(enlistment, resolver.ParseUrlOrFriendlyName(enlistment.RepoUrl + "/"));
            this.ValidateIsNone(enlistment, resolver.ParseUrlOrFriendlyName(enlistment.RepoUrl + "//"));
            this.ValidateIsNone(enlistment, resolver.ParseUrlOrFriendlyName(enlistment.RepoUrl.ToUpper()));
            this.ValidateIsNone(enlistment, resolver.ParseUrlOrFriendlyName(enlistment.RepoUrl.ToUpper() + "/"));
            this.ValidateIsNone(enlistment, resolver.ParseUrlOrFriendlyName(enlistment.RepoUrl.ToLower()));
            this.ValidateIsNone(enlistment, resolver.ParseUrlOrFriendlyName(enlistment.RepoUrl.ToLower() + "/"));

            CacheServerInfo resolvedCacheServer;
            string error;
            resolver.TryResolveUrlFromRemote(CacheServerInfo.ReservedNames.None, this.CreateGVFSConfig(), out resolvedCacheServer, out error)
                .ShouldEqual(false, "Should not succeed in resolving the name 'None'");

            resolvedCacheServer.ShouldEqual(null);
            error.ShouldNotBeNull();
        }

        [TestCase]
        public void CanParseAndResolveDefaultWhenServerAdvertisesNullListOfCacheServers()
        {
            MockGVFSEnlistment enlistment = this.CreateEnlistment();
            CacheServerResolver resolver = this.CreateResolver(enlistment);

            CacheServerInfo resolvedCacheServer;
            string error;
            resolver.TryResolveUrlFromRemote(CacheServerInfo.ReservedNames.Default, this.CreateDefaultDeserializedGVFSConfig(), out resolvedCacheServer, out error)
                .ShouldEqual(true);

            this.ValidateIsNone(enlistment, resolvedCacheServer);
        }

        [TestCase]
        public void CanParseAndResolveOtherWhenServerAdvertisesNullListOfCacheServers()
        {
            MockGVFSEnlistment enlistment = this.CreateEnlistment();
            CacheServerResolver resolver = this.CreateResolver(enlistment);

            CacheServerInfo resolvedCacheServer;
            string error;
            resolver.TryResolveUrlFromRemote(CacheServerInfo.ReservedNames.None, this.CreateDefaultDeserializedGVFSConfig(), out resolvedCacheServer, out error)
                .ShouldEqual(false, "Should not succeed in resolving the name 'None'");

            resolvedCacheServer.ShouldEqual(null);
            error.ShouldNotBeNull();
        }

        private void ValidateIsNone(Enlistment enlistment, CacheServerInfo cacheServer)
        {
            cacheServer.Url.ShouldEqual(enlistment.RepoUrl);
            cacheServer.Name.ShouldEqual(CacheServerInfo.ReservedNames.None);
        }

        private MockGVFSEnlistment CreateEnlistment(string newConfigValue = null, string oldConfigValue = null)
        {
            MockGitProcess gitProcess = new MockGitProcess();
            gitProcess.SetExpectedCommandResult(
                "config --local gvfs.cache-server",
                () => new GitProcess.Result(newConfigValue ?? string.Empty, string.Empty, newConfigValue != null ? GitProcess.Result.SuccessCode : GitProcess.Result.GenericFailureCode));
            gitProcess.SetExpectedCommandResult(
                "config gvfs.mock:..repourl.cache-server-url",
                () => new GitProcess.Result(oldConfigValue ?? string.Empty, string.Empty, oldConfigValue != null ? GitProcess.Result.SuccessCode : GitProcess.Result.GenericFailureCode));

            return new MockGVFSEnlistment(gitProcess);
        }

        private ServerGVFSConfig CreateGVFSConfig()
        {
            return new ServerGVFSConfig
            {
                CacheServers = new[]
                {
                    new CacheServerInfo(CacheServerUrl, CacheServerName, globalDefault: true),
                }
            };
        }

        private ServerGVFSConfig CreateDefaultDeserializedGVFSConfig()
        {
            return JsonConvert.DeserializeObject<ServerGVFSConfig>("{}");
        }

        private CacheServerResolver CreateResolver(MockGVFSEnlistment enlistment = null)
        {
            enlistment = enlistment ?? this.CreateEnlistment();
            return new CacheServerResolver(new MockTracer(), enlistment);
        }
    }
}
