using System.Text.RegularExpressions;

namespace GVFS.Common
{
    public class AzDevOpsOrgFromNuGetFeed
    {
        /// <summary>
        /// Given a URL for a NuGet feed hosted on Azure DevOps,
        /// return the organization that hosts the feed.
        /// </summary>
        public static bool TryParseOrg(string packageFeedUrl, out string orgName)
        {
            // We expect a URL of the form https://pkgs.dev.azure.com/{org}
            // and want to convert it to a URL of the form https://{org}.visualstudio.com
            Regex packageUrlRegex = new Regex(
                @"^https://pkgs.dev.azure.com/(?<org>.+?)/",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            Match urlMatch = packageUrlRegex.Match(packageFeedUrl);

            if (!urlMatch.Success)
            {
                orgName = null;
                return false;
            }

            orgName = urlMatch.Groups["org"].Value;
            return true;
        }

        /// <summary>
        /// Given a URL for a NuGet feed hosted on Azure DevOps,
        /// return a URL that Git Credential Manager can use to
        /// query for a credential that is valid for use with the
        /// NuGet feed.
        /// </summary>
        public static bool TryCreateCredentialQueryUrl(string packageFeedUrl, out string azureDevOpsUrl, out string error)
        {
            if (!TryParseOrg(packageFeedUrl, out string org))
            {
                azureDevOpsUrl = null;
                error = $"Input URL {packageFeedUrl} did not match expected format for an Azure DevOps Package Feed URL";
                return false;
            }

            azureDevOpsUrl = $"https://{org}.visualstudio.com";
            error = null;

            return true;
        }
    }
}
