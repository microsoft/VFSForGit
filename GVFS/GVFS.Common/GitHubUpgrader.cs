using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace GVFS.Common
{
    public class GitHubUpgrader : IProductUpgrader
    {
        private const string GitHubReleaseURL = @"https://api.github.com/repos/microsoft/vfsforgit/releases";
        private const string JSONMediaType = @"application/vnd.github.v3+json";
        private const string UserAgent = @"GVFS_Auto_Upgrader";
        private const string CommonInstallerArgs = "/VERYSILENT /CLOSEAPPLICATIONS /SUPPRESSMSGBOXES /NORESTART";
        private const string GVFSInstallerArgs = CommonInstallerArgs + " /MOUNTREPOS=false";
        private const string GitInstallerArgs = CommonInstallerArgs + " /ALLOWDOWNGRADE=1";
        private const string GitAssetId = "Git";
        private const string GVFSAssetId = "GVFS";
        private const string GitInstallerFileNamePrefix = "Git-";
        private const int RepoMountFailureExitCode = 17;

        private Version newestVersion;
        private Release newestRelease;
        private PhysicalFileSystem fileSystem;
        private ITracer tracer;
        private LocalUpgraderServices localUpgradeServices;
        private Version installedVersion;

        public GitHubUpgrader(
            string currentVersion,
            ITracer tracer,
            GitHubUpgraderConfig upgraderConfig)
        {
            this.Config = upgraderConfig;

            this.installedVersion = new Version(currentVersion);
            this.fileSystem = new PhysicalFileSystem();
            this.tracer = tracer;

            this.localUpgradeServices = new LocalUpgraderServices(tracer);
        }

        public GitHubUpgraderConfig Config { get; private set; }

        public static GitHubUpgrader Create(ITracer tracer, out string error)
        {
            return Create(new LocalGVFSConfig(), tracer, out error);
        }

        public static GitHubUpgrader Create(LocalGVFSConfig localConfig, ITracer tracer, out string error)
        {
            GitHubUpgrader upgrader = null;
            GitHubUpgraderConfig gitHubUpgraderConfig = new GitHubUpgraderConfig(tracer, localConfig);

            if (!gitHubUpgraderConfig.TryLoad(out error))
            {
                return null;
            }

            if (gitHubUpgraderConfig.ConfigError())
            {
                gitHubUpgraderConfig.ConfigAlertMessage(out error);
                return null;
            }

            upgrader = new GitHubUpgrader(
                    ProcessHelper.GetCurrentProcessVersion(),
                    tracer,
                    gitHubUpgraderConfig);

            return upgrader;
        }

        public bool UpgradeAllowed(out string message)
        {
            return this.Config.UpgradeAllowed(out message);
        }

        public bool TryQueryNewestVersion(
            out Version newVersion,
            out string message)
        {
            List<Release> releases;

            newVersion = null;
            if (this.TryFetchReleases(out releases, out message))
            {
                foreach (Release nextRelease in releases)
                {
                    Version releaseVersion = null;

                    if (nextRelease.Ring <= this.Config.UpgradeRing &&
                        nextRelease.TryParseVersion(out releaseVersion) &&
                        releaseVersion > this.installedVersion)
                    {
                        newVersion = releaseVersion;
                        this.newestVersion = releaseVersion;
                        this.newestRelease = nextRelease;
                        message = $"New GVFS version {newVersion.ToString()} available in ring {this.Config.UpgradeRing}.";
                        break;
                    }
                }

                if (newVersion == null)
                {
                    message = $"Great news, you're all caught up on upgrades in the {this.Config.UpgradeRing} ring!";
                }

                return true;
            }

            return false;
        }

        public bool TryDownloadNewestVersion(out string errorMessage)
        {
            bool downloadedGit = false;
            bool downloadedGVFS = false;
            foreach (Asset asset in this.newestRelease.Assets)
            {
                bool targetOSMatch = string.Equals(Path.GetExtension(asset.Name), GVFSPlatform.Instance.Constants.InstallerExtension, StringComparison.OrdinalIgnoreCase);
                bool isGitAsset = this.IsGitAsset(asset);
                bool isGVFSAsset = isGitAsset ? false : this.IsGVFSAsset(asset);
                if (!targetOSMatch || (!isGVFSAsset && !isGitAsset))
                {
                    continue;
                }

                if (!this.TryDownloadAsset(asset, out errorMessage))
                {
                    errorMessage = $"Could not download {(isGVFSAsset ? GVFSAssetId : GitAssetId)} installer. {errorMessage}";
                    return false;
                }
                else
                {
                    downloadedGit = isGitAsset ? true : downloadedGit;
                    downloadedGVFS = isGVFSAsset ? true : downloadedGVFS;
                }
            }

            if (!downloadedGit || !downloadedGVFS)
            {
                errorMessage = $"Could not find {(!downloadedGit ? GitAssetId : GVFSAssetId)} installer in the latest release.";
                return false;
            }

            errorMessage = null;
            return true;
        }

        public bool TryRunInstaller(InstallActionWrapper installActionWrapper, out string error)
        {
            string localError;

            this.TryGetGitVersion(out GitVersion newGitVersion, out localError);

            if (!installActionWrapper(
                 () =>
                 {
                     if (!this.TryInstallUpgrade(GitAssetId, newGitVersion.ToString(), out localError))
                     {
                         return false;
                     }

                     return true;
                 },
                $"Installing Git version: {newGitVersion}"))
            {
                error = localError;
                return false;
            }

            if (!installActionWrapper(
                 () =>
                 {
                     if (!this.TryInstallUpgrade(GVFSAssetId, this.newestVersion.ToString(), out localError))
                     {
                         return false;
                     }

                     return true;
                 },
                $"Installing GVFS version: {this.newestVersion}"))
            {
                error = localError;
                return false;
            }

            this.LogVersionInfo(this.newestVersion, newGitVersion, "Newly Installed Version");

            error = null;
            return true;
        }

        public bool TrySetupToolsDirectory(out string upgraderToolPath, out string error)
        {
            return this.localUpgradeServices.TrySetupToolsDirectory(out upgraderToolPath, out error);
        }

        public bool TryCleanup(out string error)
        {
            error = string.Empty;
            if (this.newestRelease == null)
            {
                return true;
            }

            foreach (Asset asset in this.newestRelease.Assets)
            {
                Exception exception;
                if (!this.TryDeleteDownloadedAsset(asset, out exception))
                {
                    error += $"Could not delete {asset.LocalPath}. {exception.ToString()}." + Environment.NewLine;
                }
            }

            if (!string.IsNullOrEmpty(error))
            {
                error.TrimEnd(Environment.NewLine.ToCharArray());
                return false;
            }

            error = null;
            return true;
        }

        protected virtual bool TryDeleteDownloadedAsset(Asset asset, out Exception exception)
        {
            return this.fileSystem.TryDeleteFile(asset.LocalPath, out exception);
        }

        protected virtual bool TryDownloadAsset(Asset asset, out string errorMessage)
        {
            errorMessage = null;

            string downloadPath = ProductUpgraderInfo.GetAssetDownloadsPath();
            Exception exception;
            if (!LocalUpgraderServices.TryCreateDirectory(downloadPath, out exception))
            {
                errorMessage = exception.Message;
                this.TraceException(exception, nameof(this.TryDownloadAsset), $"Error creating download directory {downloadPath}.");
                return false;
            }

            string localPath = Path.Combine(downloadPath, asset.Name);
            WebClient webClient = new WebClient();

            try
            {
                webClient.DownloadFile(asset.DownloadURL, localPath);
                asset.LocalPath = localPath;
            }
            catch (WebException webException)
            {
                errorMessage = "Download error: " + exception.Message;
                this.TraceException(webException, nameof(this.TryDownloadAsset), $"Error downloading asset {asset.Name}.");
                return false;
            }

            return true;
        }

        protected virtual bool TryFetchReleases(out List<Release> releases, out string errorMessage)
        {
            HttpClient client = new HttpClient();

            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(JSONMediaType));
            client.DefaultRequestHeaders.Add("User-Agent", UserAgent);

            releases = null;
            errorMessage = null;

            try
            {
                Stream result = client.GetStreamAsync(GitHubReleaseURL).GetAwaiter().GetResult();

                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(List<Release>));
                releases = serializer.ReadObject(result) as List<Release>;
                return true;
            }
            catch (HttpRequestException exception)
            {
                errorMessage = string.Format("Network error: could not connect to GitHub({0}). {1}", GitHubReleaseURL, exception.Message);
                this.TraceException(exception, nameof(this.TryFetchReleases), $"Error fetching release info.");
            }
            catch (SerializationException exception)
            {
                errorMessage = string.Format("Parse error: could not parse releases info from GitHub({0}). {1}", GitHubReleaseURL, exception.Message);
                this.TraceException(exception, nameof(this.TryFetchReleases), $"Error parsing release info.");
            }

            return false;
        }

        private bool TryGetGitVersion(out GitVersion gitVersion, out string error)
        {
            error = null;

            foreach (Asset asset in this.newestRelease.Assets)
            {
                if (asset.Name.StartsWith(GitInstallerFileNamePrefix) &&
                    GitVersion.TryParseInstallerName(asset.Name, GVFSPlatform.Instance.Constants.InstallerExtension, out gitVersion))
                {
                    return true;
                }
            }

            error = "Could not find Git version info in newest release";
            gitVersion = null;
            return false;
        }

        private bool TryInstallUpgrade(string assetId, string version, out string consoleError)
        {
            bool installSuccess = false;
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Upgrade Step", nameof(this.TryInstallUpgrade));
            metadata.Add("AssetId", assetId);
            metadata.Add("Version", version);

            using (ITracer activity = this.tracer.StartActivity($"{nameof(this.TryInstallUpgrade)}", EventLevel.Informational, metadata))
            {
                if (!this.TryRunInstaller(assetId, out installSuccess, out consoleError) ||
                !installSuccess)
                {
                    this.tracer.RelatedError(metadata, $"{nameof(this.TryInstallUpgrade)} failed. {consoleError}");
                    return false;
                }

                activity.RelatedInfo("Successfully installed GVFS version: " + version);
            }

            return installSuccess;
        }

        private bool TryRunInstaller(string assetId, out bool installationSucceeded, out string error)
        {
            error = null;
            installationSucceeded = false;

            int exitCode = 0;
            bool launched = this.TryRunInstallerForAsset(assetId, out exitCode, out error);
            installationSucceeded = exitCode == 0;

            return launched;
        }

        private bool TryRunInstallerForAsset(string assetId, out int installerExitCode, out string error)
        {
            error = null;
            installerExitCode = 0;

            bool installerIsRun = false;
            string path;
            string installerArgs;
            if (this.TryGetLocalInstallerPath(assetId, out path, out installerArgs))
            {
                string logFilePath = GVFSEnlistment.GetNewLogFileName(ProductUpgraderInfo.GetLogDirectoryPath(), Path.GetFileNameWithoutExtension(path));
                string args = installerArgs + " /Log=" + logFilePath;
                this.localUpgradeServices.RunInstaller(path, args, out installerExitCode, out error);

                if (installerExitCode != 0 && string.IsNullOrEmpty(error))
                {
                    error = assetId + " installer failed. Error log: " + logFilePath;
                }

                installerIsRun = true;
            }
            else
            {
                error = "Could not find downloaded installer for " + assetId;
            }

            return installerIsRun;
        }

        private bool TryGetLocalInstallerPath(string assetId, out string path, out string args)
        {
            foreach (Asset asset in this.newestRelease.Assets)
            {
                if (string.Equals(Path.GetExtension(asset.Name), GVFSPlatform.Instance.Constants.InstallerExtension, StringComparison.OrdinalIgnoreCase))
                {
                    path = asset.LocalPath;
                    if (assetId == GitAssetId && this.IsGitAsset(asset))
                    {
                        args = GitInstallerArgs;
                        return true;
                    }

                    if (assetId == GVFSAssetId && this.IsGVFSAsset(asset))
                    {
                        args = GVFSInstallerArgs;
                        return true;
                    }
                }
            }

            path = null;
            args = null;
            return false;
        }

        private bool IsGVFSAsset(Asset asset)
        {
            return this.AssetInstallerNameCompare(asset, ProductUpgraderInfo.PossibleGVFSInstallerNamePrefixes().ToArray());
        }

        private bool IsGitAsset(Asset asset)
        {
            return this.AssetInstallerNameCompare(asset, GitInstallerFileNamePrefix);
        }

        private bool AssetInstallerNameCompare(Asset asset, params string[] expectedFileNamePrefixes)
        {
            foreach (string fileNamePrefix in expectedFileNamePrefixes)
            {
                if (asset.Name.StartsWith(fileNamePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void TraceException(Exception exception, string method, string message)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Method", method);
            metadata.Add("Exception", exception.ToString());
            this.tracer.RelatedError(metadata, message, Keywords.Telemetry);
        }

        private void LogVersionInfo(
            Version gvfsVersion,
            GitVersion gitVersion,
            string message)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add(nameof(gvfsVersion), gvfsVersion.ToString());
            metadata.Add(nameof(gitVersion), gitVersion.ToString());

            this.tracer.RelatedEvent(EventLevel.Informational, message, metadata);
        }

        public class GitHubUpgraderConfig
        {
            public GitHubUpgraderConfig(ITracer tracer, LocalGVFSConfig localGVFSConfig)
            {
                this.Tracer = tracer;
                this.LocalConfig = localGVFSConfig;
            }

            public enum RingType
            {
                // The values here should be ascending.
                // Invalid - User has set an incorrect ring
                // NoConfig - User has Not set any ring yet
                // None - User has set a valid "None" ring
                // (Fast should be greater than Slow,
                //  Slow should be greater than None, None greater than Invalid.)
                // This is required for the correct implementation of Ring based
                // upgrade logic.
                Invalid = 0,
                NoConfig = None - 1,
                None = 10,
                Slow = None + 1,
                Fast = Slow + 1,
            }

            public RingType UpgradeRing { get; private set; }
            public LocalGVFSConfig LocalConfig { get; private set; }
            private ITracer Tracer { get; set; }

            public bool TryLoad(out string error)
            {
                this.UpgradeRing = RingType.NoConfig;

                string ringConfig = null;
                if (this.LocalConfig.TryGetConfig(GVFSConstants.LocalGVFSConfig.UpgradeRing, out ringConfig, out error))
                {
                    RingType ringType;
                    if (Enum.TryParse(ringConfig, ignoreCase: true, result: out ringType) &&
                        Enum.IsDefined(typeof(RingType), ringType) &&
                        ringType != RingType.Invalid)
                    {
                        this.UpgradeRing = ringType;
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(ringConfig))
                        {
                            this.UpgradeRing = RingType.Invalid;
                        }
                    }

                    return true;
                }

                error = "Could not read GVFS Config." + Environment.NewLine;
                error += GVFSConstants.UpgradeVerbMessages.SetUpgradeRingCommand;
                return false;
            }

            public bool ConfigError()
            {
                return this.UpgradeRing == RingType.Invalid;
            }

            public bool UpgradeAllowed(out string message)
            {
                if (this.UpgradeRing == RingType.Slow || this.UpgradeRing == RingType.Fast)
                {
                    message = null;
                    return true;
                }

                this.ConfigAlertMessage(out message);
                return false;
            }

            public void ConfigAlertMessage(out string message)
            {
                message = null;

                if (this.UpgradeRing == GitHubUpgraderConfig.RingType.None)
                {
                    message = GVFSConstants.UpgradeVerbMessages.NoneRingConsoleAlert + Environment.NewLine + GVFSConstants.UpgradeVerbMessages.SetUpgradeRingCommand;
                }

                if (this.UpgradeRing == GitHubUpgraderConfig.RingType.NoConfig)
                {
                    message = GVFSConstants.UpgradeVerbMessages.NoRingConfigConsoleAlert + Environment.NewLine + GVFSConstants.UpgradeVerbMessages.SetUpgradeRingCommand;
                }

                if (this.UpgradeRing == GitHubUpgraderConfig.RingType.Invalid)
                {
                    string ring;
                    string error;
                    string prefix = string.Empty;
                    if (this.LocalConfig.TryGetConfig(GVFSConstants.LocalGVFSConfig.UpgradeRing, out ring, out error))
                    {
                        prefix = $"Invalid upgrade ring `{ring}` specified in gvfs config. ";
                    }

                    message = prefix + Environment.NewLine + GVFSConstants.UpgradeVerbMessages.SetUpgradeRingCommand;
                }
            }
        }

        [DataContract(Name = "asset")]
        protected class Asset
        {
            [DataMember(Name = "name")]
            public string Name { get; set; }

            [DataMember(Name = "size")]
            public long Size { get; set; }

            [DataMember(Name = "browser_download_url")]
            public Uri DownloadURL { get; set; }

            [IgnoreDataMember]
            public string LocalPath { get; set; }
        }

        [DataContract(Name = "release")]
        protected class Release
        {
            [DataMember(Name = "name")]
            public string Name { get; set; }

            [DataMember(Name = "tag_name")]
            public string Tag { get; set; }

            [DataMember(Name = "prerelease")]
            public bool PreRelease { get; set; }

            [DataMember(Name = "assets")]
            public List<Asset> Assets { get; set; }

            [IgnoreDataMember]
            public GitHubUpgraderConfig.RingType Ring
            {
                get
                {
                    return this.PreRelease == true ? GitHubUpgraderConfig.RingType.Fast : GitHubUpgraderConfig.RingType.Slow;
                }
            }

            public bool TryParseVersion(out Version version)
            {
                version = null;

                if (this.Tag.StartsWith("v", StringComparison.CurrentCultureIgnoreCase))
                {
                    return Version.TryParse(this.Tag.Substring(1), out version);
                }

                return false;
            }
        }
    }
}
