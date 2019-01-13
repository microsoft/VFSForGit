using GVFS.Common.Tracing;
using NuGet.Common;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.Common.NuGetUpgrader
{
    /// <summary>
    /// Handles interactions with a NuGet Feed.
    /// </summary>
    public class NuGetFeed
    {
        private readonly ITracer tracer;
        private readonly string feedUrl;
        private readonly string feedName;
        private readonly string downloadFolder;

        private string personalAccessToken;
        private SourceCacheContext sourceCacheContext;
        private ILogger nuGetLogger;
        private SourceRepository sourceRepository;

        public NuGetFeed(
            string feedUrl,
            string feedName,
            string downloadFolder,
            string personalAccessToken,
            ITracer tracer)
        {
            this.feedUrl = feedUrl;
            this.feedName = feedName;
            this.downloadFolder = downloadFolder;
            this.personalAccessToken = personalAccessToken;
            this.tracer = tracer;

            // Configure the NuGet SourceCacheContext -
            // - Direct download packages - do not download
            //   to global NuGet cache.
            // - NoCache - Do not cache package version lists
            this.sourceCacheContext = NullSourceCacheContext.Instance;
            this.sourceCacheContext.NoCache = true;

            this.nuGetLogger = new Logger(this.tracer);

            this.sourceRepository = Repository.Factory.GetCoreV3(this.feedUrl);
            if (!string.IsNullOrEmpty(this.personalAccessToken))
            {
                this.sourceRepository.PackageSource.Credentials = this.Credentials;
            }
        }

        private NuGet.Configuration.PackageSourceCredential Credentials
        {
            get
            {
                return NuGet.Configuration.PackageSourceCredential.FromUserInput(
                    "VfsForGitNugetUpgrader",
                    "PersonalAccessToken",
                    this.personalAccessToken,
                    false);
            }
        }

        /// <summary>
        /// Query a NuGet feed for list of packages that match the packageId.
        /// </summary>
        /// <param name="packageId"></param>
        /// <returns>List of packages that match query parameters</returns>
        public virtual async Task<IList<IPackageSearchMetadata>> QueryFeedAsync(string packageId)
        {
            PackageMetadataResource packageMetadataResource = await this.sourceRepository.GetResourceAsync<PackageMetadataResource>();
            IEnumerable<IPackageSearchMetadata> queryResults = await packageMetadataResource.GetMetadataAsync(
                packageId,
                includePrerelease: true,
                includeUnlisted: true,
                sourceCacheContext: this.sourceCacheContext,
                log: this.nuGetLogger,
                token: CancellationToken.None);

            // TODO: consider working with just the IEnumerable
            return queryResults.ToList();
        }

        /// <summary>
        /// Download the specified packageId from the NuGet feed.
        /// </summary>
        /// <param name="packageId">PackageIdentity to download.</param>
        /// <returns>Path to the downloaded package.</returns>
        public virtual async Task<string> DownloadPackage(PackageIdentity packageId)
        {
            PackageDownloadContext packageDownloadContext = new PackageDownloadContext(
                this.sourceCacheContext,
                this.downloadFolder,
                true);

            DownloadResource downloadResource = await this.sourceRepository.GetResourceAsync<DownloadResource>();
            DownloadResourceResult downloadResourceResult = await downloadResource.GetDownloadResourceResultAsync(
                packageId,
                packageDownloadContext,
                globalPackagesFolder: string.Empty,
                logger: this.nuGetLogger,
                token: CancellationToken.None);

            // Check download result status

            string downloadPath = Path.Combine(this.downloadFolder, $"{this.feedName}.zip");

            using (FileStream fileStream = File.Create(downloadPath))
            {
                downloadResourceResult.PackageStream.CopyTo(fileStream);
            }

            return downloadPath;
        }

        /// <summary>
        /// Implementation of logger used by NuGet library. It takes all output
        /// and redirects it to the GVFS logger.
        /// </summary>
        public class Logger : ILogger
        {
            private ITracer tracer;

            public Logger(ITracer tracer)
            {
                this.tracer = tracer;
            }

            public void Log(LogLevel level, string data)
            {
                string message = $"NuGet Logger: ({level}): {data}";
                switch (level)
                {
                case LogLevel.Debug:
                case LogLevel.Verbose:
                case LogLevel.Minimal:
                case LogLevel.Information:
                    this.tracer.RelatedInfo(message);
                    break;
                 case LogLevel.Warning:
                     this.tracer.RelatedWarning(message);
                    break;
                case LogLevel.Error:
                    this.tracer.RelatedError(message);
                    break;
                default:
                    this.tracer.RelatedWarning(message);
                    break;
                }
            }

            public void Log(ILogMessage message)
            {
                this.Log(message.Level, message.Message);
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
                this.Log(LogLevel.Debug, data);
            }

            public void LogError(string data)
            {
                this.Log(LogLevel.Error, data);
            }

            public void LogInformation(string data)
            {
                this.Log(LogLevel.Information, data);
            }

            public void LogInformationSummary(string data)
            {
                this.Log(LogLevel.Information, data);
            }

            public void LogMinimal(string data)
            {
                this.Log(LogLevel.Minimal, data);
            }

            public void LogVerbose(string data)
            {
                this.Log(LogLevel.Verbose, data);
            }

            public void LogWarning(string data)
            {
                this.Log(LogLevel.Warning, data);
            }
        }
    }
}
