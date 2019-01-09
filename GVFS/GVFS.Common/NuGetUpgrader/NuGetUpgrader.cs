using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using NuGet.Protocol.Core.Types;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace GVFS.Common.NuGetUpgrader
{
    public class NuGetUpgrader : IProductUpgrader
    {
        private static readonly string GitBinPath = GVFSPlatform.Instance.GitInstallation.GetInstalledGitBinPath();

        private ITracer tracer;
        private PhysicalFileSystem fileSystem;
        private LocalUpgraderServices localUpgradeServices;
        private Version installedVersion;

        private NugetUpgraderConfig nugetUpgraderConfig;
        private ReleaseManifest releaseManifest;
        private NuGetFeed nugetFeed;
        private IPackageSearchMetadata latestVersion;
        private string downloadedPackagePath;

        public NuGetUpgrader(
            string currentVersion,
            ITracer tracer,
            NugetUpgraderConfig config,
            string downloadFolder,
            string personalAccessToken)
            : this(
                currentVersion,
                tracer,
                config,
                new PhysicalFileSystem(),
                new NuGetFeed(config.FeedUrl, config.PackageFeedName, downloadFolder, personalAccessToken, tracer))
        {
        }

        public NuGetUpgrader(
            string currentVersion,
            ITracer tracer,
            NugetUpgraderConfig config,
            PhysicalFileSystem fileSystem,
            NuGetFeed nuGetFeed)
            : this(
                currentVersion,
                tracer,
                config,
                fileSystem,
                nuGetFeed,
                new LocalUpgraderServices(tracer, fileSystem))
        {
        }

        public NuGetUpgrader(
            string currentVersion,
            ITracer tracer,
            NugetUpgraderConfig config,
            PhysicalFileSystem fileSystem,
            NuGetFeed nuGetFeed,
            LocalUpgraderServices localUpgraderServices)
        {
            this.nugetUpgraderConfig = config;
            this.tracer = tracer;
            this.installedVersion = new Version(currentVersion);

            this.fileSystem = fileSystem;
            this.nugetFeed = nuGetFeed;
            this.localUpgradeServices = localUpgraderServices;
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

        public bool UpgradeAllowed(out string message)
        {
            if (string.IsNullOrEmpty(this.nugetUpgraderConfig.FeedUrl))
            {
                message = "Nuget Feed URL has not been configured";
                return false;
            }

            if (string.IsNullOrEmpty(this.nugetUpgraderConfig.PackageFeedName))
            {
                message = "URL to lookup credentials has not been configured";
                return false;
            }

            if (string.IsNullOrEmpty(this.nugetUpgraderConfig.FeedUrlForCredentials))
            {
                message = "URL to lookup credentials has not been configured";
                return false;
            }

            message = null;
            return true;
        }

        public bool TryQueryNewestVersion(out Version newVersion, out string message)
        {
            try
            {
                IList<IPackageSearchMetadata> queryResults = this.nugetFeed.QueryFeed(this.nugetUpgraderConfig.PackageFeedName).GetAwaiter().GetResult();

                // Find the latest package
                IPackageSearchMetadata highestVersion = null;
                foreach (IPackageSearchMetadata result in queryResults)
                {
                    if (highestVersion == null || result.Identity.Version > highestVersion.Identity.Version)
                    {
                        highestVersion = result;
                    }
                }

                if (highestVersion != null &&
                    highestVersion.Identity.Version.Version > this.installedVersion)
                {
                    this.latestVersion = highestVersion;
                }

                newVersion = this.latestVersion?.Identity?.Version?.Version;

                if (!(newVersion is null))
                {
                    message = $"New version {highestVersion.Identity.Version} is available.";
                    return true;
                }
                else if (!(highestVersion is null))
                {
                    message = $"Latest available version is {highestVersion.Identity.Version}, you are up-to-date";
                    return true;
                }
                else
                {
                    message = $"No versions available via feed.";
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
                newVersion = null;
            }

            return false;
        }

        public bool TryDownloadNewestVersion(out string errorMessage)
        {
            // Check that we have latest version

            try
            {
                this.downloadedPackagePath = this.nugetFeed.DownloadPackage(this.latestVersion.Identity).GetAwaiter().GetResult();
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
            bool success = this.fileSystem.TryDeleteDirectory(this.localUpgradeServices.TempPath, out e);

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

            string upgradesDirectoryPath = ProductUpgraderInfo.GetUpgradesDirectoryPath();

            Exception e;
            if (!this.fileSystem.TryDeleteDirectory(this.localUpgradeServices.TempPath, out e))
            {
                error = e.Message;
                return false;
            }

            string extractedPackagePath = this.UnzipPackageToTempLocation();
            this.releaseManifest = ReleaseManifest.FromJsonFile(Path.Combine(extractedPackagePath, "content", "install-manifest.json"));

            this.fileSystem.CreateDirectory(upgradesDirectoryPath);

            foreach (ManifestEntry entry in this.releaseManifest.PlatformInstallManifests["Windows"].InstallActions)
            {
                installActionWrapper(
                    () =>
                    {
                        string installerPath = Path.Combine(extractedPackagePath, "content", entry.RelativePath);
                        this.localUpgradeServices.RunInstaller(installerPath, entry.Args, out installerExitCode, out localError);

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

        public bool TrySetupToolsDirectory(out string upgraderToolPath, out string error)
        {
            return this.localUpgradeServices.TrySetupToolsDirectory(out upgraderToolPath, out error);
        }

        private static bool TryGetPersonalAccessToken(string gitBinaryPath, string credentialUrl, ITracer tracer, out string token, out string error)
        {
            GitProcess gitProcess = new GitProcess(gitBinaryPath, null, null);
            return gitProcess.TryGetCredentials(tracer, credentialUrl, out string username, out token, out error);
        }

        private string UnzipPackageToTempLocation()
        {
            string extractedPackagePath = this.localUpgradeServices.TempPath;
            ZipFile.ExtractToDirectory(this.downloadedPackagePath, extractedPackagePath);
            return extractedPackagePath;
        }

        public class NugetUpgraderConfig
        {
            public NugetUpgraderConfig(ITracer tracer, LocalGVFSConfig localGVFSConfig)
            {
                this.Tracer = tracer;
                this.LocalConfig = localGVFSConfig;
            }

            public NugetUpgraderConfig(
                ITracer tracer,
                LocalGVFSConfig localGVFSConfig,
                string feedUrl,
                string packageFeedName,
                string feedUrlForCredentials)
                : this(tracer, localGVFSConfig)
            {
                this.FeedUrl = feedUrl;
                this.PackageFeedName = packageFeedName;
                this.FeedUrlForCredentials = feedUrlForCredentials;
            }

            public string FeedUrl { get; private set; }
            public string PackageFeedName { get; private set; }
            public string FeedUrlForCredentials { get; private set; }
            private ITracer Tracer { get; set; }
            private LocalGVFSConfig LocalConfig { get; set; }

            public bool TryLoad(out bool isEnabled, out bool isConfigured, out string error)
            {
                error = string.Empty;

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
    }
}
