using RGFS.Common;
using RGFS.Common.Git;
using RGFS.Common.Http;
using RGFS.Tests.Should;
using RGFS.UnitTests.Mock.Common;
using RGFS.UnitTests.Mock.Git;
using NUnit.Framework;

namespace RGFS.UnitTests.Common
{
    [TestFixture]
    public class CacheServerResolverTests
    {
        private const string CacheServerUrl = "https://cache/server";
        private const string CacheServerName = "TestCacheServer";

        private const string NoneFriendlyName = "None";
        private const string DefaultFriendlyName = "Default";
        private const string UserDefinedFriendlyName = "User Defined";

        [TestCase]
        public void CanGetCacheServerFromNewConfig()
        {
            MockEnlistment enlistment = this.CreateEnlistment(CacheServerUrl);
            CacheServerInfo cacheServer = CacheServerResolver.GetCacheServerFromConfig(enlistment);

            cacheServer.Url.ShouldEqual(CacheServerUrl);
            CacheServerResolver.GetUrlFromConfig(enlistment).ShouldEqual(CacheServerUrl);
        }

        [TestCase]
        public void CanGetCacheServerFromOldConfig()
        {
            MockEnlistment enlistment = this.CreateEnlistment(null, CacheServerUrl);
            CacheServerInfo cacheServer = CacheServerResolver.GetCacheServerFromConfig(enlistment);

            cacheServer.Url.ShouldEqual(CacheServerUrl);
            CacheServerResolver.GetUrlFromConfig(enlistment).ShouldEqual(CacheServerUrl);
        }

        [TestCase]
        public void CanGetCacheServerWithNoConfig()
        {
            MockEnlistment enlistment = this.CreateEnlistment();

            this.ValidateIsNone(enlistment, CacheServerResolver.GetCacheServerFromConfig(enlistment));
            CacheServerResolver.GetUrlFromConfig(enlistment).ShouldEqual(enlistment.RepoUrl);
        }

        [TestCase]
        public void CanResolveUrlForKnownName()
        {
            CacheServerResolver resolver = this.CreateResolver();

            CacheServerInfo resolvedCacheServer;
            string error;
            resolver.TryResolveUrlFromRemote(CacheServerName, this.CreateRGFSConfig(), out resolvedCacheServer, out error);

            resolvedCacheServer.Url.ShouldEqual(CacheServerUrl);
            resolvedCacheServer.Name.ShouldEqual(CacheServerName);
        }

        [TestCase]
        public void CanResolveNameFromKnownUrl()
        {
            CacheServerResolver resolver = this.CreateResolver();
            CacheServerInfo resolvedCacheServer = resolver.ResolveNameFromRemote(CacheServerUrl, this.CreateRGFSConfig());

            resolvedCacheServer.Url.ShouldEqual(CacheServerUrl);
            resolvedCacheServer.Name.ShouldEqual(CacheServerName);
        }

        [TestCase]
        public void CanResolveNameFromCustomUrl()
        {
            const string CustomUrl = "https://not/a/known/cache/server";

            CacheServerResolver resolver = this.CreateResolver();
            CacheServerInfo resolvedCacheServer = resolver.ResolveNameFromRemote(CustomUrl, this.CreateRGFSConfig());

            resolvedCacheServer.Url.ShouldEqual(CustomUrl);
            resolvedCacheServer.Name.ShouldEqual(UserDefinedFriendlyName);
        }

        [TestCase]
        public void CanParseUrl()
        {
            CacheServerResolver resolver = new CacheServerResolver(new MockTracer(), this.CreateEnlistment());
            CacheServerInfo parsedCacheServer = resolver.ParseUrlOrFriendlyName(CacheServerUrl);

            parsedCacheServer.Url.ShouldEqual(CacheServerUrl);
            parsedCacheServer.Name.ShouldEqual(null);
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
            parsedCacheServer.Name.ShouldEqual(DefaultFriendlyName);

            CacheServerInfo resolvedCacheServer;
            string error;
            resolver.TryResolveUrlFromRemote(parsedCacheServer.Name, this.CreateRGFSConfig(), out resolvedCacheServer, out error);

            resolvedCacheServer.Url.ShouldEqual(CacheServerUrl);
            resolvedCacheServer.Name.ShouldEqual(CacheServerName);
        }

        [TestCase]
        public void CanParseAndResolveNoCacheServer()
        {
            MockEnlistment enlistment = this.CreateEnlistment();
            CacheServerResolver resolver = this.CreateResolver(enlistment);

            this.ValidateIsNone(enlistment, resolver.ParseUrlOrFriendlyName(NoneFriendlyName));
            this.ValidateIsNone(enlistment, resolver.ParseUrlOrFriendlyName(enlistment.RepoUrl));

            CacheServerInfo resolvedCacheServer;
            string error;
            resolver.TryResolveUrlFromRemote(NoneFriendlyName, this.CreateRGFSConfig(), out resolvedCacheServer, out error)
                .ShouldEqual(false, "Should not succeed in resolving the name 'None'");

            resolvedCacheServer.ShouldEqual(null);
            error.ShouldNotBeNull();
        }

        [TestCase]
        public void HasResolvedName()
        {
            new CacheServerInfo(null, null).HasResolvedName().ShouldBeFalse();
            new CacheServerInfo(null, CacheServerInfo.ReservedNames.None).HasResolvedName().ShouldBeFalse();
            new CacheServerInfo(null, CacheServerInfo.ReservedNames.Default).HasResolvedName().ShouldBeFalse();
            new CacheServerInfo(null, CacheServerInfo.ReservedNames.UserDefined).HasResolvedName().ShouldBeFalse();
            new CacheServerInfo(null, "MyCacheServer").HasResolvedName().ShouldBeTrue();
        }

        private void ValidateIsNone(Enlistment enlistment, CacheServerInfo cacheServer)
        {
            cacheServer.Url.ShouldEqual(enlistment.RepoUrl);
            cacheServer.Name.ShouldEqual(NoneFriendlyName);
        }

        private MockEnlistment CreateEnlistment(string newConfigValue = null, string oldConfigValue = null)
        {
            MockGitProcess gitProcess = new MockGitProcess();
            gitProcess.SetExpectedCommandResult(
                "config --local rgfs.cache-server", 
                () => new GitProcess.Result(newConfigValue ?? string.Empty, string.Empty, newConfigValue != null ? GitProcess.Result.SuccessCode : GitProcess.Result.GenericFailureCode));
            gitProcess.SetExpectedCommandResult(
                "config rgfs.mock:\\repourl.cache-server-url",
                () => new GitProcess.Result(oldConfigValue ?? string.Empty, string.Empty, oldConfigValue != null ? GitProcess.Result.SuccessCode : GitProcess.Result.GenericFailureCode));

            return new MockEnlistment(gitProcess);
        }

        private RGFSConfig CreateRGFSConfig()
        {
            return new RGFSConfig
            {
                CacheServers = new[]
                {
                    new CacheServerInfo(CacheServerUrl, CacheServerName, globalDefault: true),
                }
            };
        }

        private CacheServerResolver CreateResolver(MockEnlistment enlistment = null)
        {
            enlistment = enlistment ?? this.CreateEnlistment();
            return new CacheServerResolver(new MockTracer(), enlistment);
        }
    }
}
