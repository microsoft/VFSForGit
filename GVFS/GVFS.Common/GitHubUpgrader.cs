using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography.X509Certificates;

namespace GVFS.Common
{
    public class GitHubUpgrader : ProductUpgrader
    {
        private const string GitHubReleaseURL = @"https://api.github.com/repos/microsoft/vfsforgit/releases";
        private const string JSONMediaType = @"application/vnd.github.v3+json";
        private const string UserAgent = @"GVFS_Auto_Upgrader";
        private const string GitAssetId = "Git";
        private const string GVFSAssetId = "GVFS";
        private const string GitInstallerFileNamePrefix = "Git-";
        private const string PreAndPostInstallerError = "Pre & post installers are not supported in GVFS releases from Github.";

        private Release newestRelease;
        private InstallerCertificateVerifier installerVerifier;

        public GitHubUpgrader(
            string currentVersion,
            ITracer tracer, 
            GitHubUpgraderConfig upgraderConfig, 
            InstallerCertificateVerifier verifier)
            : base(currentVersion, tracer)
        {
            this.Config = upgraderConfig;
            this.installerVerifier = verifier;
        }

        public GitHubUpgraderConfig Config { get; private set; }

        public static bool TryCreateGitHubUpgrader(
            ITracer tracer,
            out bool isEnabled,
            out bool isConfigured,
            out ProductUpgrader upgrader,
            out string error)
        {
            LocalGVFSConfig localConfig = new LocalGVFSConfig();
            GitHubUpgraderConfig gitHubUpgraderConfig = new GitHubUpgraderConfig(tracer, localConfig);

            if (gitHubUpgraderConfig.TryLoad(out isEnabled, out isConfigured, out error))
            {
                upgrader = new GitHubUpgrader(
                    ProcessHelper.GetCurrentProcessVersion(), 
                    tracer, 
                    gitHubUpgraderConfig,
                    new InstallerCertificateVerifier());
                return true;
            }

            upgrader = null;
            return false;
        }

        public Version QueryLatestVersion()
        {
            Version version;
            string consoleMessage;
            string error;
            this.TryGetNewerVersion(out version, out consoleMessage, out error);
            return version;
        }

        public void DownloadLatestVersion()
        {
            string error;
            this.TryDownloadNewestVersion(out error);
        }

        public void InstallLatestVersion()
        {
            string error;
            this.TryDownloadNewestVersion(out error);
        }

        public override bool Initialize(out string errorMessage)
        {
            errorMessage = null;
            return true;
        }

        public override bool CanRunUsingCurrentConfig(
            out bool isConfigError, 
            out string consoleMessage,
            out string errorMessage)
        {
            if (this.Config.UpgradeRing == GitHubUpgraderConfig.RingType.None)
            {
                isConfigError = false;
                consoleMessage = GVFSConstants.UpgradeVerbMessages.NoneRingConsoleAlert + Environment.NewLine + GVFSConstants.UpgradeVerbMessages.SetUpgradeRingCommand;
                errorMessage = null;
                return false;
            }

            if (this.Config.UpgradeRing == GitHubUpgraderConfig.RingType.NoConfig)
            {
                isConfigError = false;
                consoleMessage = GVFSConstants.UpgradeVerbMessages.NoRingConfigConsoleAlert + Environment.NewLine + GVFSConstants.UpgradeVerbMessages.SetUpgradeRingCommand;
                errorMessage = null;
                return false;
            }

            if (this.Config.UpgradeRing == GitHubUpgraderConfig.RingType.Invalid)
            {
                string ring;
                string error;
                if (!this.Config.LocalConfig.TryGetConfig(GVFSConstants.LocalGVFSConfig.UpgradeRing, out ring, out error))
                {
                    ring = "invalid";
                }

                consoleMessage = null;
                errorMessage = $"Invalid upgrade ring `{ring}` specified in gvfs config." + Environment.NewLine + GVFSConstants.UpgradeVerbMessages.SetUpgradeRingCommand;
                isConfigError = true;
                return false;
            }

            isConfigError = false;
            consoleMessage = null;
            errorMessage = null;
            return true;
        }

        public override bool TryGetNewerVersion(
            out Version newVersion,
            out string consoleMessage,
            out string errorMessage)
        {
            List<Release> releases;

            newVersion = null;
            consoleMessage = null;
            bool isConfigError;
            if (!this.CanRunUsingCurrentConfig(out isConfigError, out consoleMessage, out errorMessage))
            {
                return !isConfigError;
            }

            if (this.TryFetchReleases(out releases, out errorMessage))
            {
                foreach (Release nextRelease in releases)
                {
                    Version releaseVersion = null;

                    if (nextRelease.Ring <= this.Config.UpgradeRing &&
                        nextRelease.TryParseVersion(out releaseVersion) &&
                        releaseVersion > this.InstalledVersion)
                    {
                        newVersion = releaseVersion;
                        this.newestRelease = nextRelease;
                        consoleMessage = $"New GVFS version {newVersion.ToString()} available in ring {this.Config.UpgradeRing}.";
                        break;
                    }
                }
                
                if (newVersion == null)
                {
                    consoleMessage = $"Great news, you're all caught up on upgrades in the {this.Config.UpgradeRing} ring!";
                }

                return true;
            }

            return false;
        }

        public override bool TryGetGitVersion(out GitVersion gitVersion, out string error)
        {
            error = null;

            foreach (Asset asset in this.newestRelease.Assets)
            {
                if (asset.Name.StartsWith(GitInstallerFileNamePrefix) &&
                    GitVersion.TryParseInstallerName(asset.Name, ProductUpgrader.InstallerExtension, out gitVersion))
                {
                    return true;
                }
            }

            error = "Could not find Git version info in newest release";
            gitVersion = null;
            return false;
        }

        public override bool TryDownloadNewestVersion(out string errorMessage)
        {
            bool downloadedGit = false;
            bool downloadedGVFS = false;
            foreach (Asset asset in this.newestRelease.Assets)
            {
                bool targetOSMatch = string.Equals(Path.GetExtension(asset.Name), ProductUpgrader.InstallerExtension, StringComparison.OrdinalIgnoreCase);
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

        public override bool TryRunGitInstaller(out bool installationSucceeded, out string error)
        {
            error = null;
            installationSucceeded = false;

            int exitCode = 0;
            bool launched = this.TryRunInstallerForAsset(GitAssetId, out exitCode, out error);
            installationSucceeded = exitCode == 0;

            return launched;
        }

        public override bool TryRunGVFSInstaller(out bool installationSucceeded, out string error)
        {
            error = null;
            installationSucceeded = false;

            int exitCode = 0;
            bool launched = this.TryRunInstallerForAsset(GVFSAssetId, out exitCode, out error);
            installationSucceeded = exitCode == 0 || exitCode == ProductUpgrader.RepoMountFailureExitCode;

            return launched;
        }

        public override bool TryCleanup(out string error)
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
            return this.FileSystem.TryDeleteFile(asset.LocalPath, out exception);
        }

        protected virtual bool TryDownloadAsset(Asset asset, out string errorMessage)
        {
            errorMessage = null;

            string downloadPath = GetAssetDownloadsPath();
            Exception exception;
            if (!GitHubUpgrader.TryCreateDirectory(downloadPath, out exception))
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

        private bool TryRunInstallerForAsset(string assetId, out int installerExitCode, out string error)
        {
            error = null;
            installerExitCode = 0;

            bool installerIsRun = false;
            string path;
            string installerArgs;
            string gitSigner = "CN=Johannes Schindelin, O=Johannes Schindelin";
            string gvfsSigner = "CN=Microsoft Corporation, O=Microsoft Corporation";
            string expectedPublisher = assetId == GitAssetId ? gitSigner : assetId == GVFSAssetId ? gvfsSigner : null;

            if (this.TryGetLocalInstallerPath(assetId, out path, out installerArgs))
            {
                FileStream installerHandle = null;
                bool isValidated = true;
                if (this.installerVerifier != null)
                {
                    isValidated = this.installerVerifier.TryGetValidatedFileStream(expectedPublisher, path, out installerHandle, out error);
                }
                
                if (isValidated)
                {
                    string logFilePath = GVFSEnlistment.GetNewLogFileName(GetLogDirectoryPath(), Path.GetFileNameWithoutExtension(path));
                    string args = installerArgs + " /Log=" + logFilePath;
                    this.RunInstaller(path, args, out installerExitCode, out error);

                    if (installerExitCode != 0 && string.IsNullOrEmpty(error))
                    {
                        error = assetId + " installer failed. Error log: " + logFilePath;
                    }

                    installerIsRun = true;
                }
                else
                {
                    installerIsRun = false;
                }

                if (installerHandle != null)
                {
                    installerHandle.Close();
                }
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
                if (string.Equals(Path.GetExtension(asset.Name), ProductUpgrader.InstallerExtension, StringComparison.OrdinalIgnoreCase))
                {
                    path = asset.LocalPath;
                    if (assetId == GitAssetId && this.IsGitAsset(asset))
                    {
                        args = ProductUpgrader.GitInstallerArgs;
                        return true;
                    }

                    if (assetId == GVFSAssetId && this.IsGVFSAsset(asset))
                    {
                        args = ProductUpgrader.GVFSInstallerArgs;
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
            return this.AssetInstallerNameCompare(asset, ProductUpgrader.GVFSInstallerFileNamePrefix, ProductUpgrader.VFSForGitInstallerFileNamePrefix);
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

        public class InstallerCertificateVerifier
        {
            public bool TryGetValidatedFileStream(
                string expectedPublisher, 
                string path, 
                out FileStream validatedStream, 
                out string error)
            {
                try
                {
                    validatedStream = null;
                    FileStream fileStream = File.OpenRead(path);
                    X509Certificate signer = X509Certificate.CreateFromSignedFile(path);
                    X509Certificate2 certificate = new X509Certificate2(signer);
                    var certificateChain = new X509Chain
                    {
                        ChainPolicy =
                        {
                            RevocationFlag = X509RevocationFlag.EntireChain,
                            RevocationMode = X509RevocationMode.Online,
                            UrlRetrievalTimeout = new TimeSpan(0, 1, 0),
                            VerificationFlags = X509VerificationFlags.NoFlag
                        }
                    };

                    bool chainIsValid = certificateChain.Build(certificate);
                    if (!chainIsValid)
                    {
                        error = $"Verification of {path} failed. Invalid certificate chain.";
                        fileStream.Close();
                        return false;
                    }

                    if (!certificate.SubjectName.Name.StartsWith(expectedPublisher, StringComparison.InvariantCultureIgnoreCase))
                    {
                        error = $"Verification of {path} failed. Invalid certificate.";
                        fileStream.Close();
                        return false;
                    }

                    error = null;
                    validatedStream = fileStream;
                    return true;
                }
                catch (Exception ex)
                {
                    error = $"Verification of {path} failed. {ex.ToString()}.";
                    validatedStream = null;
                    return false;
                }
            }
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

            public bool TryLoad(out bool isEnabled, out bool isConfigured, out string error)
            {
                isEnabled = false;
                isConfigured = false;

                string ringConfig = null;
                if (this.LocalConfig.TryGetConfig(GVFSConstants.LocalGVFSConfig.UpgradeRing, out ringConfig, out error))
                {
                    RingType ringType;
                    if (Enum.TryParse(ringConfig, ignoreCase: true, result: out ringType) &&
                        Enum.IsDefined(typeof(RingType), ringType) &&
                        ringType != RingType.Invalid)
                    {
                        this.UpgradeRing = ringType;
                        isEnabled = true;
                        isConfigured = true;
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(ringConfig))
                        {
                            isEnabled = true;
                            this.UpgradeRing = RingType.Invalid;
                        }
                        else
                        {
                            this.UpgradeRing = RingType.NoConfig;
                        }
                    }

                    return true;
                }

                error = "Could not read GVFS Config." + Environment.NewLine;
                error += GVFSConstants.UpgradeVerbMessages.SetUpgradeRingCommand;
                return false;
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