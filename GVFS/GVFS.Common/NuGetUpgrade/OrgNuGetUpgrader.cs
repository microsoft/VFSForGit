using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace GVFS.Common.NuGetUpgrade
{
    public class OrgNuGetUpgrader : NuGetUpgrader
    {
        private HttpClient httpClient;
        private string platform;

        public OrgNuGetUpgrader(
           string currentVersion,
           ITracer tracer,
           PhysicalFileSystem fileSystem,
           HttpClient httpClient,
           bool dryRun,
           bool noVerify,
           OrgNuGetUpgraderConfig config,
           string downloadFolder,
           string platform,
           ICredentialStore credentialStore)
           : base(
               currentVersion,
               tracer,
               fileSystem,
               dryRun,
               noVerify,
               config,
               downloadFolder,
               credentialStore)
        {
            this.httpClient = httpClient;
            this.platform = platform;
        }

        public OrgNuGetUpgrader(
           string currentVersion,
           ITracer tracer,
           PhysicalFileSystem fileSystem,
           HttpClient httpClient,
           bool dryRun,
           bool noVerify,
           OrgNuGetUpgraderConfig config,
           string platform,
           NuGetFeed nuGetFeed,
           ICredentialStore credentialStore)
           : base(
               currentVersion,
               tracer,
               dryRun,
               noVerify,
               fileSystem,
               config,
               nuGetFeed,
               credentialStore,
               GVFSPlatform.Instance.CreateProductUpgraderPlatformInteractions(fileSystem, tracer))
        {
            this.httpClient = httpClient;
            this.platform = platform;
        }

        public override bool SupportsAnonymousVersionQuery { get => true; }

        private OrgNuGetUpgraderConfig Config { get => this.nuGetUpgraderConfig as OrgNuGetUpgraderConfig;  }
        private string OrgInfoServerUrl { get => this.Config.OrgInfoServer; }
        private string Ring { get => this.Config.UpgradeRing; }

        public static bool TryCreate(
            ITracer tracer,
            PhysicalFileSystem fileSystem,
            LocalGVFSConfig gvfsConfig,
            HttpClient httpClient,
            ICredentialStore credentialStore,
            bool dryRun,
            bool noVerify,
            out OrgNuGetUpgrader upgrader,
            out string error)
        {
            OrgNuGetUpgraderConfig upgraderConfig = new OrgNuGetUpgraderConfig(tracer, gvfsConfig);
            upgrader = null;

            if (!upgraderConfig.TryLoad(out error))
            {
                upgrader = null;
                return false;
            }

            if (!upgraderConfig.IsConfigured(out error))
            {
                return false;
            }

            if (!upgraderConfig.IsReady(out error))
            {
                return false;
            }

            string platform = GVFSPlatform.Instance.Name;

            upgrader = new OrgNuGetUpgrader(
                ProcessHelper.GetCurrentProcessVersion(),
                tracer,
                fileSystem,
                httpClient,
                dryRun,
                noVerify,
                upgraderConfig,
                ProductUpgraderInfo.GetAssetDownloadsPath(),
                platform,
                credentialStore);

            return true;
        }

        public override bool TryQueryNewestVersion(out Version newVersion, out string message)
        {
            newVersion = null;

            if (!AzDevOpsOrgFromNuGetFeed.TryParseOrg(this.Config.FeedUrl, out string orgName))
            {
                message = "OrgNuGetUpgrader is not able to parse org name from NuGet Package Feed URL";
                return false;
            }

            OrgInfoApiClient infoServer = new OrgInfoApiClient(this.httpClient, this.OrgInfoServerUrl);

            try
            {
                this.highestVersionAvailable = infoServer.QueryNewestVersion(orgName, this.platform, this.Ring);
            }
            catch (Exception exception) when (exception is HttpRequestException ||
                                              exception is TaskCanceledException)
            {
                // GetStringAsync can also throw a TaskCanceledException to indicate a timeout
                // https://github.com/dotnet/corefx/issues/20296
                message = string.Format("Network error: could not connect to server ({0}). {1}", this.OrgInfoServerUrl, exception.Message);
                this.TraceException(exception, nameof(this.TryQueryNewestVersion), "Error connecting to server.");

                return false;
            }
            catch (SerializationException exception)
            {
                message = string.Format("Parse error: could not parse response from server({0}). {1}", this.OrgInfoServerUrl, exception.Message);
                this.TraceException(exception, nameof(this.TryQueryNewestVersion), "Error parsing response from server.");

                return false;
            }
            catch (Exception exception) when (exception is ArgumentException ||
                                              exception is FormatException ||
                                              exception is OverflowException)
            {
                message = string.Format("Unexpected response from server: could nor parse version({0}). {1}", this.OrgInfoServerUrl, exception.Message);
                this.TraceException(exception, nameof(this.TryQueryNewestVersion), "Error parsing response from server.");

                return false;
            }

            if (this.highestVersionAvailable != null &&
                this.highestVersionAvailable > this.installedVersion)
            {
                newVersion = this.highestVersionAvailable;
            }

            if (newVersion != null)
            {
                this.tracer.RelatedInfo($"{nameof(this.TryQueryNewestVersion)} - new version available: installedVersion: {this.installedVersion}, highestVersionAvailable: {newVersion}");
                message = $"New version {newVersion} is available.";
                return true;
            }
            else if (this.highestVersionAvailable != null)
            {
                this.tracer.RelatedInfo($"{nameof(this.TryQueryNewestVersion)} - up-to-date");
                message = $"Highest version available is {this.highestVersionAvailable}, you are up-to-date";
                return true;
            }
            else
            {
                this.tracer.RelatedInfo($"{nameof(this.TryQueryNewestVersion)} - no versions available from feed.");
                message = "No versions available via endpoint.";
                return true;
            }
        }

        public class OrgNuGetUpgraderConfig : NuGetUpgraderConfig
        {
            public OrgNuGetUpgraderConfig(ITracer tracer, LocalGVFSConfig localGVFSConfig)
                : base(tracer, localGVFSConfig)
            {
            }

            public string OrgInfoServer { get; set; }

            public string UpgradeRing { get; set; }

            public override bool TryLoad(out string error)
            {
                if (!base.TryLoad(out error))
                {
                    return false;
                }

                if (!this.localConfig.TryGetConfig(GVFSConstants.LocalGVFSConfig.OrgInfoServerUrl, out string orgInfoServerUrl, out error))
                {
                    this.tracer.RelatedError(error);
                    return false;
                }

                this.OrgInfoServer = orgInfoServerUrl;

                if (!this.localConfig.TryGetConfig(GVFSConstants.LocalGVFSConfig.UpgradeRing, out string upgradeRing, out error))
                {
                    this.tracer.RelatedError(error);
                    return false;
                }

                this.UpgradeRing = upgradeRing;

                return true;
            }

            public override bool IsReady(out string error)
            {
                if (!base.IsReady(out error) ||
                    string.IsNullOrEmpty(this.UpgradeRing) ||
                    string.IsNullOrEmpty(this.OrgInfoServer))
                {
                    error = string.Join(
                        Environment.NewLine,
                        "One or more required settings for OrgNuGetUpgrader are missing.",
                        "Use `gvfs config [{GVFSConstants.LocalGVFSConfig.UpgradeFeedUrl} | {GVFSConstants.LocalGVFSConfig.UpgradeFeedPackageName} | {GVFSConstants.LocalGVFSConfig.UpgradeRing} | {GVFSConstants.LocalGVFSConfig.OrgInfoServerUrl}] <value>` to set the config.");
                    return false;
                }

                return true;
            }
        }
    }
}
