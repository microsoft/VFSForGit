using GVFS.Common.Git;
using GVFS.Common.Tracing;
using NuGet.Common;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.Common
{
    public class NuGetPackageUpgrader : ProductUpgraderBase
    {
        public NuGetPackageUpgrader(
            string currentVersion,
            ITracer tracer,
            string feedUrl,
            string feedName,
            string downloadFolder,
            string personalAccessToken)
            : base(currentVersion, tracer)
        {
            this.FeedUrl = feedUrl;
            this.FeedName = feedName;
            this.DownloadFolder = downloadFolder;
            this.PersonalAccessToken = personalAccessToken;
        }

        private string FeedUrl { get; set; }

        private string FeedName { get; set; }

        private string DownloadFolder { get; set; }

        private string PersonalAccessToken { get; set; }

        private IPackageSearchMetadata LatestVersion { get; set; }

        private NuGetPackageManifest Manifest { get; set; }

        private string PackagePath { get; set; }

        private string ExtractedPath { get; set; }

        public Version QueryLatestVersion()
        {
            Version version;
            string error;
            this.TryGetNewerVersion(out version, out error);
            return version;
        }

        public override bool Initialize(out string errorMessage)
        {
            errorMessage = null;
            return true;
        }

        public override bool TryGetNewerVersion(out Version newVersion, out string errorMessage)
        {
            newVersion = null;
            errorMessage = null;

            IList<IPackageSearchMetadata> queryResults = this.QueryFeed(this.FeedName).GetAwaiter().GetResult();

            // Find the latest package
            IPackageSearchMetadata highestVersion = null;
            foreach (IPackageSearchMetadata result in queryResults)
            {
                if (highestVersion == null || result.Identity.Version > highestVersion.Identity.Version)
                {
                    highestVersion = result;
                }
            }

            if (highestVersion != null)
            {
                this.LatestVersion = highestVersion;
                newVersion = this.LatestVersion.Identity?.Version?.Version;
                return true;
            }

            return false;
        }

        public override bool TryGetGitVersion(out GitVersion gitVersion, out string error)
        {
            gitVersion = new GitVersion(1, 1, 1, "Windows", 1, 1);
            error = null;
            return false;
        }

        public override bool TryDownloadNewestVersion(out string errorMessage)
        {
            this.PackagePath = this.DownloadPackage(this.LatestVersion.Identity).GetAwaiter().GetResult();

            Exception e;
            bool success = this.TryDeleteDirectory(ProductUpgraderBase.GetTempPath(), out e);

            this.UnzpiPackageToTempLocation();

            this.Manifest = new NuGetPackageManifest();
            this.Manifest.Read(Path.Combine(this.ExtractedPath, "install-manifest.txt"));

            errorMessage = null;
            return true;
        }

        public override bool TryRunGitInstaller(out bool installationSucceeded, out string error)
        {
            // Read the manifest, look up Git Installer
            string path = this.Manifest.Properties["Git"];
            path = Path.Combine(this.ExtractedPath, path);
            int exitCode;

            string logFilePath = GVFSEnlistment.GetNewLogFileName(GetLogDirectoryPath(), Path.GetFileNameWithoutExtension(path));
            string args = GitInstallerArgs + " /Log=" + logFilePath;
            this.RunInstaller(path, args, out exitCode, out error);

            error = null;
            installationSucceeded = false;
            return false;
        }

        public override bool TryRunGVFSInstaller(out bool installationSucceeded, out string error)
        {
            // Read the manifest, look up GVFS Installer
            string path = this.Manifest.Properties["GVFS"];
            path = Path.Combine(this.ExtractedPath, path);
            int exitCode;

            string logFilePath = GVFSEnlistment.GetNewLogFileName(GetLogDirectoryPath(), Path.GetFileNameWithoutExtension(path));
            string args = GVFSInstallerArgs + " /Log=" + logFilePath;
            this.RunInstaller(path, args, out exitCode, out error);

            error = null;
            installationSucceeded = false;
            return false;
        }

        public override bool TryCleanup(out string error)
        {
            error = null;
            Exception e;
            bool success = this.TryDeleteDirectory(GetTempPath(), out e);

            if (!success)
            {
                error = e.Message;
            }

            return success;
        }

        private async Task<IList<IPackageSearchMetadata>> QueryFeed(string packageId)
        {
            var credentials = NuGet.Configuration.PackageSourceCredential.FromUserInput(
                "Package Tester",
                "Package Tester",
                this.PersonalAccessToken,
                false);

            SourceRepository sourceRepository = Repository.Factory.GetCoreV3(this.FeedUrl);
            sourceRepository.PackageSource.Credentials = credentials;

            var packageMetadataResource = await sourceRepository.GetResourceAsync<PackageMetadataResource>();
            var cacheContext = new SourceCacheContext();
            /// cacheContext.DirectDownload = true;
            /// cacheContext.NoCache = true;
            IList<IPackageSearchMetadata> queryResults = (await packageMetadataResource.GetMetadataAsync(packageId, true, true, cacheContext, new Logger(), CancellationToken.None)).ToList();
            return queryResults;
        }

        private async Task<string> DownloadPackage(PackageIdentity packageId)
        {
            var credentials = NuGet.Configuration.PackageSourceCredential.FromUserInput(
                "Package Tester",
                "Package Tester",
                this.PersonalAccessToken,
                false);

            SourceRepository sourceRepository = Repository.Factory.GetCoreV3(this.FeedUrl);
            sourceRepository.PackageSource.Credentials = credentials;

            var downloadResource = await sourceRepository.GetResourceAsync<DownloadResource>();
            var downloadResourceResult = await downloadResource.GetDownloadResourceResultAsync(packageId, new PackageDownloadContext(new SourceCacheContext(), this.DownloadFolder, true), string.Empty, new Logger(), CancellationToken.None);

            string downloadPath = Path.Combine(this.DownloadFolder, "out.zip");

            using (var fileStream = File.Create(downloadPath))
            {
                downloadResourceResult.PackageStream.CopyTo(fileStream);
            }

            return downloadPath;
        }

        private void UnzpiPackageToTempLocation()
        {
            this.ExtractedPath = ProductUpgraderBase.GetTempPath();

            ZipFile.ExtractToDirectory(this.PackagePath, this.ExtractedPath);
        }

        public class Logger : ILogger
        {
            public void Log(LogLevel level, string data)
            {
            }

            public void Log(ILogMessage message)
            {
            }

            public Task LogAsync(LogLevel level, string data)
            {
                return Task.CompletedTask;
            }

            public Task LogAsync(ILogMessage message)
            {
                return Task.CompletedTask;
            }

            public void LogDebug(string data)
            {
            }

            public void LogError(string data)
            {
            }

            public void LogInformation(string data)
            {
            }

            public void LogInformationSummary(string data)
            {
            }

            public void LogMinimal(string data)
            {
            }

            public void LogVerbose(string data)
            {
            }

            public void LogWarning(string data)
            {
            }
        }
    }
}
