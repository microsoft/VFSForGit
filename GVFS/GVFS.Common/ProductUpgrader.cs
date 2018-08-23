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
        private const string GitHubReleaseURL = @"https://api.github.com/repos/microsoft/gvfs/releases";
        private const string JSONMediaType = @"application/vnd.github.v3+json";
        private const string UserAgent = @"GVFS_Auto_Upgrader";
        private const string InstallerArgs = "/VERYSILENT /CLOSEAPPLICATIONS /SUPPRESSMSGBOXES /NORESTART /MOUNTREPOS=false";
        private const string GitAssetNamePrefix = "Git";
        private const string GVFSAssetNamePrefix = "GVFS";
        private const string GitInstallerFileNamePrefix = "Git-";
        private const string GVFSInstallerFileNamePrefix = "SetupGVFS";
        private const string UpgradeLockFileName = "GVFSUpgrade.Lock";
        private const string UpgradeLockFileSignature = "GVFS";
        private const int RepoMountFailureExitCode = 17;

        private Version installedVersion;
        private Release newestRelease;
        private FileBasedLock globalUpgradeLock;
        private PhysicalFileSystem fileSystem;

        public ProductUpgrader(
            string currentVersion,
            ITracer tracer)
        {
            this.installedVersion = new Version(currentVersion);
            this.fileSystem = new PhysicalFileSystem();

            string upgradesDirectoryPath = GetUpgradesDirectoryPath();
            this.fileSystem.CreateDirectory(upgradesDirectoryPath);
            this.globalUpgradeLock = new FileBasedLock(
                this.fileSystem,
                tracer,
                Path.Combine(upgradesDirectoryPath, UpgradeLockFileName),
                UpgradeLockFileSignature,
                overwriteExistingLock: true);
        }

        public enum RingType
        {
            // The values here should be ascending. 
            // (Fast should be greater than Slow, 
            //  Slow should be greater than None, None greater than Invalid.)
            // This is required for the correct implementation of Ring based 
            // upgrade logic.
            Invalid = 0,
            None = 10,
            Slow = 20,
            Fast = 30,
        }

        public RingType Ring { get; protected set; }

        public bool IsGVFSUpgradeRunning()
        {
            return !this.globalUpgradeLock.IsFree();
        }

        public bool AcquireUpgradeLock()
        {
            return this.globalUpgradeLock.TryAcquireLockAndDeleteOnClose();
        }

        public bool ReleaseUpgradeLock()
        {
            return this.globalUpgradeLock.TryReleaseLock();
        }

        public bool TryGetNewerVersion(
            out Version newVersion,
            out string errorMessage)
        {
            List<Release> releases;

            newVersion = null;
            if (!this.TryLoadRingConfig(out errorMessage))
            {
                return false;
            }

            if (this.Ring == RingType.None)
            {
                errorMessage = "Upgrade ring set to None. No upgrade check was performed.";
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
                    GitVersion.TryParseInstallerName(asset.Name, out gitVersion))
                {
                    return true;
                }
            }

            error = "Could not find Git version info in newest release";

            return false;
        }

        public bool TryDownloadNewestVersion(out string errorMessage)
        {
            errorMessage = null;

            foreach (Asset asset in this.newestRelease.Assets)
            {
                if (!this.TryDownloadAsset(asset, out errorMessage))
                {
                    return false;
                }
            }

            return true;
        }

        public bool TryRunGitInstaller(out bool installationSucceeded, out string error)
        {
            error = null;
            installationSucceeded = false;

            int exitCode = 0;
            bool launched = this.TryRunInstallerForAsset(GitAssetNamePrefix, out exitCode, out error);
            installationSucceeded = exitCode == 0;

            return launched;
        }

        public bool TryRunGVFSInstaller(out bool installationSucceeded, out string error)
        {
            error = null;
            installationSucceeded = false;

            int exitCode = 0;
            bool launched = this.TryRunInstallerForAsset(GVFSAssetNamePrefix, out exitCode, out error);
            installationSucceeded = exitCode == 0 || exitCode == RepoMountFailureExitCode;

            return launched;
        }

        public virtual bool TryCreateToolsDirectory(out string upgraderToolPath, out string error)
        {
            upgraderToolPath = null;
            error = null;

            string rootDirectoryPath = ProductUpgrader.GetUpgradesDirectoryPath();
            string toolsDirectoryPath = Path.Combine(rootDirectoryPath, ToolsDirectory);
            if (TryCreateDirectory(toolsDirectoryPath, out error))
            {
                bool success = true;
                string currentPath = ProcessHelper.GetCurrentProcessLocation();
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
                        success = false;
                        error = string.Join(Environment.NewLine, "Unauthorized access.", "Please retry gvfs upgrade from an elevated command prompt.", e.ToString());
                        break;
                    }
                    catch (IOException e)
                    {
                        success = false;
                        error = "Disk error while trying to copy upgrade tools." + Environment.NewLine + e.ToString();
                        break;
                    }
                }

                upgraderToolPath = Path.Combine(toolsDirectoryPath, UpgraderToolName);
                return success;
            }

            return false;
        }

        protected virtual bool TryLoadRingConfig(out string error)
        {
            string errorAdvisory = "Please run 'git config --system gvfs.upgrade-ring [\"Fast\"|\"Slow\"|\"None\"]' and retry.";
            string gitPath = GVFSPlatform.Instance.GitInstallation.GetInstalledGitBinPath();
            GitProcess.Result result = GitProcess.GetFromSystemConfig(gitPath, GVFSConstants.GitConfig.UpgradeRing);
            if (!result.HasErrors && !string.IsNullOrEmpty(result.Output.TrimEnd('\r', '\n')))
            {
                string ringConfig = result.Output.TrimEnd('\r', '\n');
                RingType ringType;

                if (Enum.TryParse(ringConfig, ignoreCase: true, result: out ringType) && 
                    Enum.IsDefined(typeof(RingType), ringType) &&
                    ringType != RingType.Invalid)
                {
                    this.Ring = ringType;
                    error = null;
                    return true;
                }
                else
                {
                    error = "Invalid upgrade ring type(" + ringConfig + ") specified in Git config.";
                    error += Environment.NewLine + errorAdvisory;
                }
            }
            else
            {
                error = string.IsNullOrEmpty(result.Errors) ? "Unable to determine upgrade ring." : result.Errors;
                error += Environment.NewLine + errorAdvisory;
            }

            this.Ring = RingType.Invalid;
            return false;
        }

        protected virtual bool TryDownloadAsset(Asset asset, out string errorMessage)
        {
            errorMessage = null;

            string downloadPath = GetAssetDownloadsPath();
            if (!ProductUpgrader.TryCreateDirectory(downloadPath, out errorMessage))
            {
                return false;
            }

            string localPath = Path.Combine(downloadPath, asset.Name);
            if (File.Exists(localPath) && asset.Size == new FileInfo(localPath).Length)
            {
                asset.LocalPath = localPath;
                return true;
            }

            WebClient webClient = new WebClient();

            try
            {
                webClient.DownloadFile(asset.DownloadURL, localPath);
                asset.LocalPath = localPath;
            }
            catch (WebException exception)
            {
                errorMessage = "Download error: " + exception.ToString();
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
                errorMessage = string.Format("Network error: could not connect to GitHub. {0}", exception.ToString());
            }
            catch (SerializationException exception)
            {
                errorMessage = string.Format("Parse error: could not parse releases info from GitHub. {0}", exception.ToString());
            }

            return false;
        }

        protected virtual void RunInstaller(string path, string args, out int exitCode, out string error)
        {
            ProcessResult processResult = ProcessHelper.Run(path, args);

            exitCode = processResult.ExitCode;
            error = processResult.Errors;
        }

        private bool TryRunInstallerForAsset(string name, out int exitCode, out string error)
        {
            error = null;
            exitCode = 0;

            string path = null;
            if (this.TryGetLocalInstallerPath(name, out path))
            {
                string logFilePath = GVFSEnlistment.GetNewLogFileName(GetLogDirectoryPath(), Path.GetFileNameWithoutExtension(path));
                string args = InstallerArgs + " /Log=" + logFilePath;
                this.RunInstaller(path, args, out exitCode, out error);

                if (exitCode != 0 && string.IsNullOrEmpty(error))
                {
                    error = name + " installer failed. Error log: " + logFilePath;
                }

                return true;
            }

            error = "Could not find " + name;

            return false;
        }

        private bool TryGetLocalInstallerPath(string name, out string path)
        {
            path = null;

            foreach (Asset asset in this.newestRelease.Assets)
            {
                string extension = Path.GetExtension(asset.Name);
                if (extension != null && extension == ".exe")
                {
                    if ((name == GitAssetNamePrefix && asset.Name.StartsWith(GitInstallerFileNamePrefix)) ||
                        (name == GVFSAssetNamePrefix && asset.Name.StartsWith(GVFSInstallerFileNamePrefix)))
                    {
                        path = asset.LocalPath;
                        return true;
                    }
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