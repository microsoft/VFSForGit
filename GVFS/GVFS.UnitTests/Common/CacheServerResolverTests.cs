using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.Git;
using NUnit.Framework;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class CacheServerResolverTests
    {
        private const string CacheServerUrl = "https://cache/server";
        private const string CacheServerName = "TestCacheServer";
        private const string PrefetchCacheServerUrl = "https://prefetch-cache/server";
        private const string GetCacheServerUrl = "https://get-cache/server";
        private const string PostCacheServerUrl = "https://post-cache/server";
        private const string SizesCacheServerUrl = "https://sizes-cache/server";

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
        public void EndpointSpecificCacheServersOverrideGlobalCacheServer()
        {
            MockGVFSEnlistment enlistment = this.CreateEnlistment(
                CacheServerUrl,
                prefetchCacheServerUrl: PrefetchCacheServerUrl,
                getCacheServerUrl: GetCacheServerUrl,
                postCacheServerUrl: PostCacheServerUrl,
                sizesCacheServerUrl: SizesCacheServerUrl);

            CacheServerInfo cacheServer = CacheServerResolver.GetCacheServerFromConfig(enlistment);

            cacheServer.PrefetchEndpointUrl.ShouldEqual(PrefetchCacheServerUrl + "/gvfs/prefetch");
            cacheServer.ObjectsGetEndpointUrl.ShouldEqual(GetCacheServerUrl + "/gvfs/objects");
            cacheServer.ObjectsPostEndpointUrl.ShouldEqual(PostCacheServerUrl + "/gvfs/objects");
            cacheServer.SizesEndpointUrl.ShouldEqual(SizesCacheServerUrl + "/gvfs/sizes");
        }

        [TestCase]
        public void EndpointSpecificCacheServersFallBackToGlobalCacheServer()
        {
            CacheServerInfo cacheServer = CacheServerResolver.GetCacheServerFromConfig(this.CreateEnlistment(CacheServerUrl));

            cacheServer.PrefetchEndpointUrl.ShouldEqual(CacheServerUrl + "/gvfs/prefetch");
            cacheServer.ObjectsGetEndpointUrl.ShouldEqual(CacheServerUrl + "/gvfs/objects");
            cacheServer.ObjectsPostEndpointUrl.ShouldEqual(CacheServerUrl + "/gvfs/objects");
            cacheServer.SizesEndpointUrl.ShouldEqual(CacheServerUrl + "/gvfs/sizes");
        }

        [TestCase]
        public void EndpointSpecificCacheServersArePreservedWhenGlobalCacheServerIsResolved()
        {
            CacheServerInfo configuredCacheServer = new CacheServerInfo(CacheServerUrl, CacheServerName)
                .WithEndpointOverrides(PrefetchCacheServerUrl, GetCacheServerUrl, PostCacheServerUrl, SizesCacheServerUrl);
            CacheServerInfo resolvedCacheServer = new CacheServerInfo("https://resolved-cache/server", "ResolvedCache")
                .WithEndpointOverridesFrom(configuredCacheServer);

            resolvedCacheServer.PrefetchCacheServerUrl.ShouldEqual(PrefetchCacheServerUrl);
            resolvedCacheServer.GetCacheServerUrl.ShouldEqual(GetCacheServerUrl);
            resolvedCacheServer.PostCacheServerUrl.ShouldEqual(PostCacheServerUrl);
            resolvedCacheServer.SizesCacheServerUrl.ShouldEqual(SizesCacheServerUrl);
            resolvedCacheServer.HasValidUrl().ShouldEqual(true);
        }

        [TestCase]
        public void CanSaveEndpointSpecificCacheServers()
        {
            MockGVFSEnlistment enlistment = this.CreateEnlistment();
            MockGitProcess git = (MockGitProcess)enlistment.CreateGitProcess();
            git.SetExpectedCommandResult(
                "config --local --replace-all  \"gvfs.prefetch.cache-server\" \"https://prefetch-cache/server\"",
                () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.SuccessCode));
            git.SetExpectedCommandResult(
                "config --local --replace-all  \"gvfs.get.cache-server\" \"https://get-cache/server\"",
                () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.SuccessCode));
            git.SetExpectedCommandResult(
                "config --local --replace-all  \"gvfs.post.cache-server\" \"https://post-cache/server\"",
                () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.SuccessCode));
            git.SetExpectedCommandResult(
                "config --local --replace-all  \"gvfs.sizes.cache-server\" \"https://sizes-cache/server\"",
                () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.SuccessCode));

            CacheServerInfo cacheServer = new CacheServerInfo(CacheServerUrl, CacheServerName)
                .WithEndpointOverrides(PrefetchCacheServerUrl, GetCacheServerUrl, PostCacheServerUrl, SizesCacheServerUrl);

            new CacheServerResolver(new MockTracer(), enlistment)
                .TrySaveEndpointUrlsToLocalConfig(cacheServer, out string error)
                .ShouldEqual(true, error);
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

        private MockGVFSEnlistment CreateEnlistment(
            string newConfigValue = null,
            string oldConfigValue = null,
            string prefetchCacheServerUrl = null,
            string getCacheServerUrl = null,
            string postCacheServerUrl = null,
            string sizesCacheServerUrl = null)
        {
            MockGitProcess gitProcess = new MockGitProcess();
            gitProcess.SetExpectedCommandResult(
                "config --local gvfs.cache-server",
                () => new GitProcess.Result(newConfigValue ?? string.Empty, string.Empty, newConfigValue != null ? GitProcess.Result.SuccessCode : GitProcess.Result.GenericFailureCode));
            gitProcess.SetExpectedCommandResult(
                "config gvfs.mock:..repourl.cache-server-url",
                () => new GitProcess.Result(oldConfigValue ?? string.Empty, string.Empty, oldConfigValue != null ? GitProcess.Result.SuccessCode : GitProcess.Result.GenericFailureCode));
            gitProcess.SetExpectedCommandResult(
                "config --local gvfs.prefetch.cache-server",
                () => new GitProcess.Result(prefetchCacheServerUrl ?? string.Empty, string.Empty, prefetchCacheServerUrl != null ? GitProcess.Result.SuccessCode : GitProcess.Result.GenericFailureCode));
            gitProcess.SetExpectedCommandResult(
                "config --local gvfs.get.cache-server",
                () => new GitProcess.Result(getCacheServerUrl ?? string.Empty, string.Empty, getCacheServerUrl != null ? GitProcess.Result.SuccessCode : GitProcess.Result.GenericFailureCode));
            gitProcess.SetExpectedCommandResult(
                "config --local gvfs.post.cache-server",
                () => new GitProcess.Result(postCacheServerUrl ?? string.Empty, string.Empty, postCacheServerUrl != null ? GitProcess.Result.SuccessCode : GitProcess.Result.GenericFailureCode));
            gitProcess.SetExpectedCommandResult(
                "config --local gvfs.sizes.cache-server",
                () => new GitProcess.Result(sizesCacheServerUrl ?? string.Empty, string.Empty, sizesCacheServerUrl != null ? GitProcess.Result.SuccessCode : GitProcess.Result.GenericFailureCode));

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
            return GVFSJsonOptions.Deserialize<ServerGVFSConfig>("{}");
        }

        private CacheServerResolver CreateResolver(MockGVFSEnlistment enlistment = null)
        {
            enlistment = enlistment ?? this.CreateEnlistment();
            return new CacheServerResolver(new MockTracer(), enlistment);
        }
    }
}
