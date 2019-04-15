using GVFS.Common.Git;
using GVFS.Common.Tracing;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace GVFS.Common.NuGetUpgrade
{
    public class QueryGVFSVersionFromNuGetFeed : IQueryGVFSVersion
    {
        private NuGetFeed nuGetFeed;
        private string packageFeedName;
        private string nuGetFeedUrl;
        private ITracer tracer;
        private ICredentialStore credentialStore;
        private bool isNuGetFeedInitialized;

        public QueryGVFSVersionFromNuGetFeed(
            ITracer tracer,
            ICredentialStore credentialStore,
            NuGetFeed nuGetFeed,
            string packageFeedName,
            string nuGetFeedUrl)
        {
            this.tracer = tracer;
            this.credentialStore = credentialStore;
            this.nuGetFeed = nuGetFeed;
            this.packageFeedName = packageFeedName;
            this.nuGetFeedUrl = nuGetFeedUrl;
        }

        public static bool TryCreateAzDevOrgUrlFromPackageFeedUrl(string packageFeedUrl, out string azureDevOpsUrl, out string error)
        {
            // We expect a URL of the form https://pkgs.dev.azure.com/{org}
            // and want to convert it to a URL of the form https://{org}.visualstudio.com
            Regex packageUrlRegex = new Regex(
                @"^https://pkgs.dev.azure.com/(?<org>.+?)/",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            Match urlMatch = packageUrlRegex.Match(packageFeedUrl);

            if (!urlMatch.Success)
            {
                azureDevOpsUrl = null;
                error = $"Input URL {packageFeedUrl} did not match expected format for an Azure DevOps Package Feed URL";
                return false;
            }

            string org = urlMatch.Groups["org"].Value;

            azureDevOpsUrl = urlMatch.Result($"https://{org}.visualstudio.com");
            error = null;

            return true;
        }

        public Version QueryVersion()
        {
            // Ensure NuGetFeed is Initialized
            IList<IPackageSearchMetadata> queryResults = this.QueryFeed(firstAttempt: true);

            // Find the package with the highest version
            IPackageSearchMetadata newestPackage = null;
            foreach (IPackageSearchMetadata result in queryResults)
            {
                if (newestPackage == null || result.Identity.Version > newestPackage.Identity.Version)
                {
                    newestPackage = result;
                }
            }

            return newestPackage.Identity.Version.Version;
        }

        public void DownloadVersion(Version version)
        {
            PackageIdentity packageId = this.GetPackageForVersion(version);

            if (packageId == null)
            {
                // errorMessage = "Could not find package for version. This indicates the package feed is out of sync.";
            }

            string downloadedPackagePath = this.nuGetFeed.DownloadPackageAsync(packageId).GetAwaiter().GetResult();
        }

        private static EventMetadata CreateEventMetadata(Exception e = null)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Area", nameof(NuGetFeed));
            if (e != null)
            {
                metadata.Add("Exception", e.ToString());
            }

            return metadata;
        }

        private IList<IPackageSearchMetadata> QueryFeed(bool firstAttempt)
        {
            try
            {
                return this.nuGetFeed.QueryFeedAsync(this.packageFeedName).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (firstAttempt &&
                                       this.IsAuthRelatedException(ex))
            {
                // If we fail to query the feed due to an authorization error, then it is possible we have stale
                // credentials, or credentials without the correct scope. Re-aquire fresh credentials and try again.
                EventMetadata data = CreateEventMetadata(ex);
                this.tracer.RelatedWarning(data, "Failed to query feed due to unauthorized error. Re-acquiring new credentials and trying again.");

                if (!this.TryRefreshCredentials(out string error))
                {
                    // If we were unable to re-acquire credentials, throw a new exception indicating that we tried to handle this, but were unable to.
                    throw new Exception($"Failed to query the feed for upgrade packages due to: {ex.Message}, and was not able to re-acquire new credentials due to: {error}", ex);
                }

                // Now that we have re-acquired credentials, try again - but with the retry flag set to false.
                return this.QueryFeed(firstAttempt: false);
            }
            catch (Exception ex)
            {
                EventMetadata data = CreateEventMetadata(ex);
                string message = $"Error encountered when querying NuGet feed. Is first attempt: {firstAttempt}.";
                this.tracer.RelatedWarning(data, message);
                throw new Exception($"Failed to query the NuGet package feed due to error: {ex.Message}", ex);
            }
        }

        private bool IsAuthRelatedException(Exception ex)
        {
            // In observation, we have seen either an HttpRequestException directly, or
            // a FatalProtocolException wrapping an HttpRequestException when we are not able
            // to auth against the NuGet feed.
            System.Net.Http.HttpRequestException httpRequestException = null;
            if (ex is System.Net.Http.HttpRequestException)
            {
                httpRequestException = ex as System.Net.Http.HttpRequestException;
            }
            else if (ex is FatalProtocolException &&
                ex.InnerException is System.Net.Http.HttpRequestException)
            {
                httpRequestException = ex.InnerException as System.Net.Http.HttpRequestException;
            }

            if (httpRequestException != null &&
                (httpRequestException.Message.Contains("401") || httpRequestException.Message.Contains("403")))
            {
                return true;
            }

            return false;
        }

        private bool TryRefreshCredentials(out string error)
        {
            try
            {
                string authUrl;
                if (!TryCreateAzDevOrgUrlFromPackageFeedUrl(this.nuGetFeedUrl, out authUrl, out error))
                {
                    return false;
                }

                if (!this.TryReacquirePersonalAccessToken(authUrl, this.tracer, out string token, out error))
                {
                    return false;
                }

                this.nuGetFeed.SetCredentials(token);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;

                // this.TraceException(ex, nameof(this.TryRefreshCredentials), "Failed to refresh credentials.");
                return false;
            }
        }

        private bool TryReacquirePersonalAccessToken(string credentialUrl, ITracer tracer, out string token, out string error)
        {
            if (!this.credentialStore.TryDeleteCredential(this.tracer, credentialUrl, username: null, password: null, error: out error))
            {
                token = null;
                return false;
            }

            return this.TryGetPersonalAccessToken(credentialUrl, tracer, out token, out error);
        }

        private PackageIdentity GetPackageForVersion(Version version)
        {
            IList<IPackageSearchMetadata> queryResults = this.QueryFeed(firstAttempt: true);

            IPackageSearchMetadata packageForVersion = null;
            foreach (IPackageSearchMetadata result in queryResults)
            {
                if (result.Identity.Version.Version == version)
                {
                    packageForVersion = result;
                    break;
                }
            }

            return packageForVersion?.Identity;
        }

        private bool TryGetPersonalAccessToken(string credentialUrl, ITracer tracer, out string token, out string error)
        {
            error = null;
            return this.credentialStore.TryGetCredential(this.tracer, credentialUrl, out string username, out token, out error);
        }

        private bool EnsureNuGetFeedInitialized(out string error)
        {
            if (!this.isNuGetFeedInitialized)
            {
                if (this.credentialStore == null)
                {
                    throw new InvalidOperationException("Attempted to call method that requires authentication but no CredentialStore is configured.");
                }

                string authUrl;
                if (!TryCreateAzDevOrgUrlFromPackageFeedUrl(this.nuGetFeedUrl, out authUrl, out error))
                {
                    return false;
                }

                if (!this.TryGetPersonalAccessToken(authUrl, this.tracer, out string token, out error))
                {
                    return false;
                }

                this.nuGetFeed.SetCredentials(token);
                this.isNuGetFeedInitialized = true;
            }

            error = null;
            return true;
        }
    }
}
