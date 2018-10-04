using GVFS.Common.FileSystem;
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

namespace GVFS.Common
{
    public partial class ProductUpgrader
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
        private const string ToolsDirectory = "Tools";
        private static readonly string UpgraderToolName = GVFSPlatform.Instance.Constants.GVFSUpgraderExecutableName;
        private static readonly string UpgraderToolConfigFile = UpgraderToolName + ".config";
        private static readonly string[] UpgraderToolAndLibs =
            {
                UpgraderToolName,
                UpgraderToolConfigFile,
                "GVFS.Common.dll",
                "GVFS.Platform.Windows.dll",
                "Microsoft.Diagnostics.Tracing.EventSource.dll",
                "netstandard.dll",
                "System.Net.Http.dll",
                "Newtonsoft.Json.dll"
            };
        
        private Version installedVersion;
        private Release newestRelease;
        private PhysicalFileSystem fileSystem;
        private ITracer tracer;

        public ProductUpgrader(
            string currentVersion,
            ITracer tracer)
        {
            this.installedVersion = new Version(currentVersion);
            this.fileSystem = new PhysicalFileSystem();
            this.Ring = RingType.Invalid;
            this.tracer = tracer;

            string upgradesDirectoryPath = GetUpgradesDirectoryPath();
            this.fileSystem.CreateDirectory(upgradesDirectoryPath);
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

        public RingType Ring { get; protected set; }
        
        public bool TryGetNewerVersion(
            out Version newVersion,
            out string errorMessage)
        {
            List<Release> releases;

            newVersion = null;
            if (this.Ring == RingType.Invalid && !this.TryLoadRingConfig(out errorMessage))
            {
                return false;
            }
            
            if (this.TryFetchReleases(out releases, out errorMessage))
            {
                foreach (Release nextRelease in releases)
                {
                    Version releaseVersion;

                    if (nextRelease.Ring <= this.Ring &&
                        nextRelease.TryParseVersion(out releaseVersion) &&
                        releaseVersion > this.installedVersion)
                    {
                        newVersion = releaseVersion;
                        this.newestRelease = nextRelease;
                        break;
                    }
                }

                return true;
            }

            return false;
        }

        public bool TryGetGitVersion(out GitVersion gitVersion, out string error)
        {
            gitVersion = null;
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

        public bool TryRunGitInstaller(out bool installationSucceeded, out string error)
        {
            error = null;
            installationSucceeded = false;

            int exitCode = 0;
            bool launched = this.TryRunInstallerForAsset(GitAssetId, out exitCode, out error);
            installationSucceeded = exitCode == 0;

            return launched;
        }

        public bool TryRunGVFSInstaller(out bool installationSucceeded, out string error)
        {
            error = null;
            installationSucceeded = false;

            int exitCode = 0;
            bool launched = this.TryRunInstallerForAsset(GVFSAssetId, out exitCode, out error);
            installationSucceeded = exitCode == 0 || exitCode == RepoMountFailureExitCode;

            return launched;
        }

        // TrySetupToolsDirectory -
        // Copies GVFS Upgrader tool and its dependencies to a temporary location in ProgramData.
        // Reason why this is needed - When GVFS.Upgrader.exe is run from C:\ProgramFiles\GVFS folder
        // upgrade installer that is downloaded and run will fail. This is because it cannot overwrite
        // C:\ProgramFiles\GVFS\GVFS.Upgrader.exe that is running. Moving GVFS.Upgrader.exe along with
        // its dependencies to a temporary location inside ProgramData and running GVFS.Upgrader.exe 
        // from this temporary location helps avoid this problem.
        public virtual bool TrySetupToolsDirectory(out string upgraderToolPath, out string error)
        {
            string rootDirectoryPath = ProductUpgrader.GetUpgradesDirectoryPath();
            string toolsDirectoryPath = Path.Combine(rootDirectoryPath, ToolsDirectory);
            Exception exception;
            if (TryCreateDirectory(toolsDirectoryPath, out exception))
            {
                string currentPath = ProcessHelper.GetCurrentProcessLocation();
                error = null;
                foreach (string name in UpgraderToolAndLibs)
                {
                    string toolPath = Path.Combine(currentPath, name);
                    string destinationPath = Path.Combine(toolsDirectoryPath, name);
                    try
                    {
                        File.Copy(toolPath, destinationPath, overwrite: true);
                    }
                    catch (UnauthorizedAccessException e)
                    {
                        error = string.Join(
                            Environment.NewLine, 
                            "File copy error - " + e.Message, 
                            $"Make sure you have write permissions to directory {rootDirectoryPath} and run {GVFSConstants.UpgradeVerbMessages.GVFSUpgradeConfirm} again.");
                        this.TraceException(e, nameof(this.TrySetupToolsDirectory), $"Error copying {toolPath} to {destinationPath}.");
                        break;
                    }
                    catch (IOException e)
                    {
                        error = "File copy error - " + e.Message;
                        this.TraceException(e, nameof(this.TrySetupToolsDirectory), $"Error copying {toolPath} to {destinationPath}.");
                        break;
                    }
                }

                upgraderToolPath = string.IsNullOrEmpty(error) ? Path.Combine(toolsDirectoryPath, UpgraderToolName) : null;
                return string.IsNullOrEmpty(error);
            }

            upgraderToolPath = null;
            error = exception.Message;
            this.TraceException(exception, nameof(this.TrySetupToolsDirectory), $"Error creating upgrade tools directory {toolsDirectoryPath}.");
            return false;
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
        
        public virtual bool TryLoadRingConfig(out string error)
        {
            string gitPath = GVFSPlatform.Instance.GitInstallation.GetInstalledGitBinPath();
            LocalGVFSConfig localConfig = new LocalGVFSConfig();

            string ringConfig = null;
            if (localConfig.TryGetConfig(GVFSConstants.LocalGVFSConfig.UpgradeRing, out ringConfig, out error, this.tracer))
            {
                RingType ringType;

                if (Enum.TryParse(ringConfig, ignoreCase: true, result: out ringType) &&
                    Enum.IsDefined(typeof(RingType), ringType) &&
                    ringType != RingType.Invalid)
                {
                    this.Ring = ringType;
                    error = null;
                    return true;
                }

                if (string.IsNullOrEmpty(ringConfig))
                {
                    this.Ring = RingType.NoConfig;
                    error = null;
                    return true;
                }

                error = "Invalid upgrade ring `" + ringConfig + "` specified in gvfs config." + Environment.NewLine;
            }
            
            error += GVFSConstants.UpgradeVerbMessages.SetUpgradeRingCommand;
            this.Ring = RingType.Invalid;
            return false;
        }

        protected virtual bool TryDeleteDownloadedAsset(Asset asset, out Exception exception)
        {
            return this.fileSystem.TryDeleteFile(asset.LocalPath, out exception);
        }

        protected virtual bool TryDownloadAsset(Asset asset, out string errorMessage)
        {
            errorMessage = null;

            string downloadPath = GetAssetDownloadsPath();
            Exception exception;
            if (!ProductUpgrader.TryCreateDirectory(downloadPath, out exception))
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

        protected virtual void RunInstaller(string path, string args, out int exitCode, out string error)
        {
            ProcessResult processResult = ProcessHelper.Run(path, args);

            exitCode = processResult.ExitCode;
            error = processResult.Errors;
        }

        private static bool TryCreateDirectory(string path, out Exception exception)
        {
            try
            {
                Directory.CreateDirectory(path);
            }
            catch (IOException e)
            {
                exception = e;
                return false;
            }
            catch (UnauthorizedAccessException e)
            {
                exception = e;
                return false;
            }

            exception = null;
            return true;
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
                error = "Could not find downloaded installer for " + assetId;
            }

            return installerIsRun;
        }

        private void TraceException(Exception exception, string method, string message)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Method", method);
            metadata.Add("Exception", exception.ToString());
            this.tracer.RelatedError(metadata, message, Keywords.Telemetry);
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
            return this.AssetInstallerNameCompare(asset, GVFSInstallerFileNamePrefix, VFSForGitInstallerFileNamePrefix);
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
            public RingType Ring
            {
                get
                {
                    return this.PreRelease == true ? RingType.Fast : RingType.Slow;
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