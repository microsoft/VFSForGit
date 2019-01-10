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
            else if (string.IsNullOrEmpty(this.nugetUpgraderConfig.PackageFeedName))
            {
                message = "URL to lookup credentials has not been configured";
                return false;
            }
            else if (string.IsNullOrEmpty(this.nugetUpgraderConfig.FeedUrlForCredentials))
            {
                message = "URL to lookup credentials has not been configured";
                return false;
            }
            else
            {
                message = null;
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
                    this.tracer.RelatedInfo($"{nameof(this.TryQueryNewestVersion)} - new version available: installedVersion: {this.installedVersion}, latestAvailableVersion: {highestVersion}");
                    message = $"New version {highestVersion.Identity.Version} is available.";
                    return true;
                }
                else if (!(highestVersion is null))
                {
                    this.tracer.RelatedInfo($"{nameof(this.TryQueryNewestVersion)} - up-to-date");
                    message = $"Latest available version is {highestVersion.Identity.Version}, you are up-to-date";
                    return true;
                }
                else
                {
                    this.tracer.RelatedInfo($"{nameof(this.TryQueryNewestVersion)} - no versions available from feed.");
                    message = $"No versions available via feed.";
                }
            }
            catch (Exception ex)
            {
                this.tracer.RelatedError($"{nameof(this.TryQueryNewestVersion)} failed with: {ex.Message}");
                message = ex.Message;
                newVersion = null;
            }

            return false;
        }

        public bool TryDownloadNewestVersion(out string errorMessage)
        {
            // Check that we have latest version

            using (ITracer activity = this.tracer.StartActivity(nameof(this.TryDownloadNewestVersion), EventLevel.Informational))
            {
                try
                {
                    this.downloadedPackagePath = this.nugetFeed.DownloadPackage(this.latestVersion.Identity).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    activity.RelatedError($"{nameof(this.TryDownloadNewestVersion)} - error encountered: ${ex.Message}");
                    errorMessage = ex.Message;
                    return false;
                }
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
                this.tracer.RelatedError($"{nameof(this.TryCleanup)} - Error encountered: {e.Message}");
                error = e.Message;
            }

            return success;
        }

        public bool TryRunInstaller(InstallActionWrapper installActionWrapper, out string error)
        {
            string localError = null;
            int installerExitCode;
            bool installSuccesesfull = true;
            using (ITracer activity = this.tracer.StartActivity(nameof(this.TryRunInstaller), EventLevel.Informational))
            {
                try
                {
                    string platformKey = ReleaseManifest.WindowsPlatformKey;
                    string upgradesDirectoryPath = ProductUpgraderInfo.GetUpgradesDirectoryPath();

                    Exception e;
                    if (!this.fileSystem.TryDeleteDirectory(this.localUpgradeServices.TempPath, out e))
                    {
                        error = e.Message;
                        return false;
                    }

                    string extractedPackagePath = this.UnzipPackageToTempLocation();
                    this.releaseManifest = ReleaseManifest.FromJsonFile(Path.Combine(extractedPackagePath, "content", "install-manifest.json"));
                    InstallManifestPlatform platformInstallManifest = this.releaseManifest.PlatformInstallManifests[platformKey];

                    if (platformInstallManifest is null)
                    {
                        activity.RelatedError($"Extracted ReleaseManifest from JSON, but there was no entry for {platformKey}.");
                        error = $"No entry in the manifest for the current platform ({platformKey}). Please verify the upgrade package.";
                        return false;
                    }

                    activity.RelatedInfo($"Extracted ReleaseManifest from JSON. InstallActions: {platformInstallManifest.InstallActions.Count}");

                    this.fileSystem.CreateDirectory(upgradesDirectoryPath);

                    foreach (ManifestEntry entry in platformInstallManifest.InstallActions)
                    {
                        string installerPath = Path.Combine(extractedPackagePath, "content", entry.InstallerRelativePath);

                        activity.RelatedInfo(
                            $"Running install action: Name: {entry.Name}, Version: {entry.Version}" +
                            $"InstallerPath: {installerPath} Args: {entry.Args}");

                        installActionWrapper(
                            () =>
                            {
                                this.localUpgradeServices.RunInstaller(installerPath, entry.Args, out installerExitCode, out localError);

                                installSuccesesfull = installerExitCode == 0;

                                return installSuccesesfull;
                            },
                            $"Installing {entry.Name} Version: {entry.Version}");
                    }
                }
                catch (Exception ex)
                {
                    localError = ex.Message;
                    installSuccesesfull = false;
                }

                if (!installSuccesesfull)
                {
                    activity.RelatedError($"Could not complete all install actions: {localError}");
                    error = localError;
                    return false;
                }
                else
                {
                    activity.RelatedInfo($"Install actions completed successfully.");
                    error = null;
                    return true;
                }
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
