using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.Collections.Generic;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.Git;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class CacheServerInfoTests
    {
        private const string DefaultCacheName = "DefaultCache";
        private const string UserSuppliedCacheName = "ValidCache";
        private const string UserSuppliedUrl = "https://validUrl";

        private static readonly IEnumerable<CacheServerInfo> KnownCaches = new List<CacheServerInfo>()
            {
                new CacheServerInfo("https://anotherValidUrl", DefaultCacheName, true),
                new CacheServerInfo(UserSuppliedUrl, UserSuppliedCacheName, false)
            };
        
        private MockEnlistment enlistment = new MockEnlistment();

        [TestCase]
        public void ParsesValidUserSuppliedUrl()
        {
            string error;
            CacheServerInfo output;
            CacheServerInfo.TryDetermineCacheServer(
                UserSuppliedUrl, 
                gitProcess: null, 
                enlistment: this.enlistment, 
                knownCaches: null, 
                output: out output,
                error: out error).ShouldBeTrue(error);
            output.Url.ShouldEqual(UserSuppliedUrl);
        }

        [TestCase]
        public void FailsToParseInvalidUserSuppliedUrl()
        {
            string error;
            CacheServerInfo output;
            CacheServerInfo.TryDetermineCacheServer(
                "invalidCacheUrl", 
                gitProcess: null, 
                enlistment: this.enlistment, 
                knownCaches: null, 
                output: out output,
                error: out error).ShouldBeFalse();
            output.ShouldBeNull();
        }

        [TestCase]
        public void ParsesUserSuppliedFriendlyName()
        {
            string error;
            CacheServerInfo output;
            CacheServerInfo.TryDetermineCacheServer(
                UserSuppliedCacheName, 
                gitProcess: null, 
                enlistment: this.enlistment, 
                knownCaches: KnownCaches, 
                output: out output,
                error: out error).ShouldBeTrue(error);
            output.Url.ShouldEqual(UserSuppliedUrl);
        }

        [TestCase]
        public void FailsToParseInvalidUserSuppliedFriendlyName()
        {
            string error;
            CacheServerInfo output;
            CacheServerInfo.TryDetermineCacheServer(
                "invalidCacheName",
                gitProcess: null,
                enlistment: this.enlistment,
                knownCaches: KnownCaches,
                output: out output,
                error: out error).ShouldBeFalse();
            output.ShouldBeNull();
        }

        [TestCase]
        public void ParsesConfiguredCacheName()
        {
            MockGitProcess git = new MockGitProcess();
            git.SetExpectedCommandResult("config gvfs.cache-server", () => new GitProcess.Result(UserSuppliedCacheName, string.Empty, GitProcess.Result.SuccessCode));

            string error;
            CacheServerInfo output;
            CacheServerInfo.TryDetermineCacheServer(
                userUrlish: null,
                gitProcess: git,
                enlistment: this.enlistment,
                knownCaches: KnownCaches,
                output: out output,
                error: out error).ShouldBeTrue(error);
            output.Url.ShouldEqual(UserSuppliedUrl);
        }

        [TestCase]
        public void ResolvesUrlIntoNone()
        {
            MockGitProcess git = new MockGitProcess();

            string error;
            CacheServerInfo output;
            CacheServerInfo.TryDetermineCacheServer(
                userUrlish: this.enlistment.RepoUrl,
                gitProcess: git,
                enlistment: this.enlistment,
                knownCaches: KnownCaches,
                output: out output,
                error: out error).ShouldBeTrue(error);

            output.Name.ShouldEqual(CacheServerInfo.NoneFriendlyName);
            output.Url.ShouldEqual(this.enlistment.RepoUrl);
        }

        [TestCase]
        public void ResolvesUrlIntoFriendlyName()
        {
            MockGitProcess git = new MockGitProcess();

            string error;
            CacheServerInfo output;
            CacheServerInfo.TryDetermineCacheServer(
                userUrlish: UserSuppliedUrl,
                gitProcess: git,
                enlistment: this.enlistment,
                knownCaches: KnownCaches,
                output: out output,
                error: out error).ShouldBeTrue(error);

            output.Name.ShouldEqual(UserSuppliedCacheName);
            output.Url.ShouldEqual(UserSuppliedUrl);
        }

        [TestCase]
        public void FallsBackToDeprecatedConfigSetting()
        {
            MockGitProcess git = new MockGitProcess();
            git.SetExpectedCommandResult(@"config gvfs.mock:\repourl.cache-server-url", () => new GitProcess.Result(UserSuppliedUrl, string.Empty, GitProcess.Result.SuccessCode));
            git.SetExpectedCommandResult(@"config --local  gvfs.cache-server " + UserSuppliedUrl, () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.SuccessCode));
            string error;
            CacheServerInfo output;
            CacheServerInfo.TryDetermineCacheServer(
                userUrlish: null,
                gitProcess: git,
                enlistment: this.enlistment,
                knownCaches: null,
                output: out output,
                error: out error).ShouldBeTrue(error);

            output.Url.ShouldEqual(UserSuppliedUrl);
        }

        [TestCase]
        public void FallsBackToDefaultCache()
        {
            MockGitProcess git = new MockGitProcess();
            git.SetExpectedCommandResult(@"config gvfs.mock:\repourl.cache-server-url", () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.GenericFailureCode));

            string error;
            CacheServerInfo output;
            CacheServerInfo.TryDetermineCacheServer(
                userUrlish: null,
                gitProcess: git,
                enlistment: this.enlistment,
                knownCaches: KnownCaches,
                output: out output,
                error: out error).ShouldBeTrue(error);

            output.Name.ShouldEqual(DefaultCacheName);
        }

        [TestCase]
        public void FallsBackToNone()
        {
            MockGitProcess git = new MockGitProcess();
            git.SetExpectedCommandResult(@"config gvfs.mock:\repourl.cache-server-url", () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.GenericFailureCode));

            string error;
            CacheServerInfo output;
            CacheServerInfo.TryDetermineCacheServer(
                userUrlish: null,
                gitProcess: git,
                enlistment: this.enlistment,
                knownCaches: null,
                output: out output,
                error: out error).ShouldBeTrue(error);

            output.Name.ShouldEqual(CacheServerInfo.NoneFriendlyName);
            output.Url.ShouldEqual(this.enlistment.RepoUrl);
        }
    }
}
