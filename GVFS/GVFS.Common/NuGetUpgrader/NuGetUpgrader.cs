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
    public class NuGetUpgrader : ProductUpgraderBase, IProductUpgrader
    {
        public NuGetUpgrader(
            string currentVersion,
            ITracer tracer,
            NugetUpgraderConfig config,
            string downloadFolder,
            string personalAccessToken)
            : base(currentVersion, tracer)
        {
            this.Config = config;
            this.DownloadFolder = downloadFolder;
            this.PersonalAccessToken = personalAccessToken;
        }

        private NugetUpgraderConfig Config { get; set; }

        private string DownloadFolder { get; set; }

        private string PersonalAccessToken { get; set; }

        private IPackageSearchMetadata LatestVersion { get; set; }

        private ReleaseManifest Manifest { get; set; }

        private string PackagePath { get; set; }

        private string ExtractedPath { get; set; }

        private NuGet.Configuration.PackageSourceCredential Credentials
        {
            get
            {
                return NuGet.Configuration.PackageSourceCredential.FromUserInput(
                    "VfsForGitNugetUpgrader",
                    "PersonalAccessToken",
                    this.PersonalAccessToken,
                    false);
            }
        }

        public static IProductUpgrader Create(
            ITracer tracer,
            out string error)
        {
            NugetUpgraderConfig upgraderConfig = new NugetUpgraderConfig(tracer, new LocalGVFSConfig());
            bool isConfigured;
            bool isEnabled;

            if (!upgraderConfig.TryLoad(out isEnabled, out isConfigured, out error))
            {
                return null;
            }

            if (!TryGetPersonalAccessToken(
                GitBinPath,
                upgraderConfig.FeedUrlForCredentials,
                tracer,
                out string token,
                out error))
            {
                return null;
            }

            NuGetUpgrader upgrader = new NuGetUpgrader(
                ProcessHelper.GetCurrentProcessVersion(),
                tracer,
                upgraderConfig,
                ProductUpgraderInfo.GetAssetDownloadsPath(),
                token);

            return upgrader;
        }

        public Version QueryLatestVersion()
        {
            Version version;
            string message;
            this.TryQueryNewestVersion(out version, out message);
            return version;
        }

        public bool UpgradeAllowed(out string error)
        {
            error = string.Empty;
            return true;
        }

        public bool TryQueryNewestVersion(out Version newVersion, out string message)
        {
            newVersion = null;
            message = null;

            IList<IPackageSearchMetadata> queryResults = this.QueryFeed(this.Config.PackageFeedName).GetAwaiter().GetResult();

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

        public bool TryDownloadNewestVersion(out string errorMessage)
        {
            try
            {
                this.PackagePath = this.DownloadPackage(this.LatestVersion.Identity).GetAwaiter().GetResult();

                Exception e;
                bool success = this.TryDeleteDirectory(ProductUpgraderBase.GetTempPath(), out e);

                this.UnzipPackageToTempLocation();
                this.Manifest = new ReleaseManifestJson();
                this.Manifest.Read(Path.Combine(this.ExtractedPath, "content", "install-manifest.json"));
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }

            errorMessage = null;
            return true;
        }

        public bool TryCleanup(out string error)
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

        public bool TryRunInstaller(InstallActionWrapper installActionWrapper, out string error)
        {
            string localError = null;
            int installerExitCode;
            bool installSuccesesfull = true;

            foreach (ManifestEntry entry in this.Manifest.ManifestEntries)
            {
                installActionWrapper(
                    () =>
                    {
                        string installerPath = Path.Combine(this.ExtractedPath, "content", entry.RelativePath);
                        this.RunInstaller(installerPath, entry.Args, out installerExitCode, out localError);

                        installSuccesesfull = installerExitCode == 0;

                        // Just for initial experiment to make sure each step is displayed in UI
                        Thread.Sleep(5 * 1000);

                        return installSuccesesfull;
                    },
                    $"Installing {entry.Name} Version: {entry.Version}");
            }

            if (!installSuccesesfull)
            {
                error = localError;
                return false;
            }
            else
            {
                error = null;
                return true;
            }
        }

        public void CleanupDownloadDirectory()
        {
            throw new NotImplementedException();
        }

        private static bool TryGetPersonalAccessToken(string gitBinaryPath, string credentialUrl, ITracer tracer, out string token, out string error)
        {
            GitProcess gitProcess = new GitProcess(gitBinaryPath, null, null);
            return gitProcess.TryGetCredentials(tracer, credentialUrl, out string username, out token, out error);
        }

        private async Task<IList<IPackageSearchMetadata>> QueryFeed(string packageId)
        {
            SourceRepository sourceRepository = Repository.Factory.GetCoreV3(this.Config.FeedUrl);
            if (!string.IsNullOrEmpty(this.PersonalAccessToken))
            {
                sourceRepository.PackageSource.Credentials = this.Credentials;
            }

            var packageMetadataResource = await sourceRepository.GetResourceAsync<PackageMetadataResource>();
            var cacheContext = new SourceCacheContext();
            cacheContext.DirectDownload = true;
            cacheContext.NoCache = true;
            IList<IPackageSearchMetadata> queryResults = (await packageMetadataResource.GetMetadataAsync(packageId, true, true, cacheContext, new Logger(this.tracer), CancellationToken.None)).ToList();
            return queryResults;
        }

        private async Task<string> DownloadPackage(PackageIdentity packageId)
        {
            SourceRepository sourceRepository = Repository.Factory.GetCoreV3(this.Config.FeedUrl);
            if (!string.IsNullOrEmpty(this.PersonalAccessToken))
            {
                sourceRepository.PackageSource.Credentials = this.Credentials;
            }

            var downloadResource = await sourceRepository.GetResourceAsync<DownloadResource>();
            var downloadResourceResult = await downloadResource.GetDownloadResourceResultAsync(packageId, new PackageDownloadContext(new SourceCacheContext(), this.DownloadFolder, true), string.Empty, new Logger(this.tracer), CancellationToken.None);

            string downloadPath = Path.Combine(this.DownloadFolder, $"{this.Config.PackageFeedName}.zip");

            using (var fileStream = File.Create(downloadPath))
            {
                downloadResourceResult.PackageStream.CopyTo(fileStream);
            }

            return downloadPath;
        }

        private void UnzipPackageToTempLocation()
        {
            this.ExtractedPath = ProductUpgraderBase.GetTempPath();
            ZipFile.ExtractToDirectory(this.PackagePath, this.ExtractedPath);
        }

        public class NugetUpgraderConfig
        {
            public NugetUpgraderConfig(ITracer tracer, LocalGVFSConfig localGVFSConfig)
            {
                this.Tracer = tracer;
                this.LocalConfig = localGVFSConfig;
            }

            public string FeedUrl { get; private set; }
            public string PackageFeedName { get; private set; }
            public string FeedUrlForCredentials { get; private set; }
            private ITracer Tracer { get; set; }
            private LocalGVFSConfig LocalConfig { get; set; }

            public bool TryLoad(out bool isEnabled, out bool isConfigured, out string error)
            {
                error = string.Empty;
                isEnabled = false;
                isConfigured = false;

                string configValue;
                string readError;
                bool feedURLAvailable = false;
                if (this.LocalConfig.TryGetConfig(GVFSConstants.LocalGVFSConfig.UpgradeFeedUrl, out configValue, out readError))
                {
                    feedURLAvailable = !string.IsNullOrEmpty(configValue);
                }
                else
                {
                    error += readError;
                }

                this.FeedUrl = configValue;

                bool credentialURLAvailable = false;
                if (this.LocalConfig.TryGetConfig(GVFSConstants.LocalGVFSConfig.UpgradeFeedCredentialUrl, out configValue, out readError))
                {
                    credentialURLAvailable = !string.IsNullOrEmpty(configValue);
                }
                else
                {
                    error += string.IsNullOrEmpty(error) ? readError : ", " + readError;
                }

                this.FeedUrlForCredentials = configValue;

                bool feedNameAvailable = false;
                if (this.LocalConfig.TryGetConfig(GVFSConstants.LocalGVFSConfig.UpgradeFeedPackageName, out configValue, out readError))
                {
                    feedNameAvailable = !string.IsNullOrEmpty(configValue);
                }
                else
                {
                    error += string.IsNullOrEmpty(error) ? readError : ", " + readError;
                }

                this.PackageFeedName = configValue;

                isEnabled = feedURLAvailable || credentialURLAvailable || feedNameAvailable;
                isConfigured = feedURLAvailable && credentialURLAvailable && feedNameAvailable;

                if (!isEnabled)
                {
                    error = string.Join(
                        Environment.NewLine,
                        "Nuget upgrade server is not configured.",
                        $"Use `gvfs config [{GVFSConstants.LocalGVFSConfig.UpgradeFeedUrl} | {GVFSConstants.LocalGVFSConfig.UpgradeFeedCredentialUrl} | {GVFSConstants.LocalGVFSConfig.UpgradeFeedPackageName}] <value>` to set the config.");
                    return false;
                }

                if (!isConfigured)
                {
                    error = string.Join(
                            Environment.NewLine,
                            "Nuget upgrade server is not configured completely.",
                            $"Use `gvfs config [{GVFSConstants.LocalGVFSConfig.UpgradeFeedUrl} | {GVFSConstants.LocalGVFSConfig.UpgradeFeedCredentialUrl} | {GVFSConstants.LocalGVFSConfig.UpgradeFeedPackageName}] <value>` to set the config.",
                            $"More config info: {error}");
                    return false;
                }

                return true;
            }
        }

        public class Logger : ILogger
        {
            private ITracer tracer;

            public Logger(ITracer tracer)
            {
                this.tracer = tracer;
            }

            public void Log(LogLevel level, string data)
            {
                this.tracer.RelatedInfo($"NuGetPackageUpgrader ({level}): {data}");
            }

            public void Log(ILogMessage message)
            {
                this.tracer.RelatedInfo($"NuGetPackageUpgrader ({message.Level}): {message.Message}");
            }

            public Task LogAsync(LogLevel level, string data)
            {
                this.Log(level, data);
                return Task.CompletedTask;
            }

            public Task LogAsync(ILogMessage message)
            {
                this.Log(message);
                return Task.CompletedTask;
            }

            public void LogDebug(string data)
            {
                this.tracer.RelatedInfo($"NuGetPackageUpgrader (Debug): {data}");
            }

            public void LogError(string data)
            {
                this.tracer.RelatedInfo($"NuGetPackageUpgrader (Error): {data}");
            }

            public void LogInformation(string data)
            {
                this.tracer.RelatedInfo($"NuGetPackageUpgrader (Information): {data}");
            }

            public void LogInformationSummary(string data)
            {
                this.tracer.RelatedInfo($"NuGetPackageUpgrader (InformationSummary): {data}");
            }

            public void LogMinimal(string data)
            {
                this.tracer.RelatedInfo($"NuGetPackageUpgrader (Minimal): {data}");
            }

            public void LogVerbose(string data)
            {
                this.tracer.RelatedInfo($"NuGetPackageUpgrader (Verbose): {data}");
            }

            public void LogWarning(string data)
            {
                this.tracer.RelatedInfo($"NuGetPackageUpgrader (Warning): {data}");
            }
        }
    }
}
