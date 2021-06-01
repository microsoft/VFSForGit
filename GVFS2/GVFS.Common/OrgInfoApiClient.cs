using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Web;

namespace GVFS.Common
{
    /// <summary>
    /// Class that handles communication with a server that contains version information.
    /// </summary>
    public class OrgInfoApiClient
    {
        private const string VersionApi = "/api/GetLatestVersion";

        private HttpClient client;
        private string baseUrl;

        public OrgInfoApiClient(HttpClient client, string baseUrl)
        {
            this.client = client;
            this.baseUrl = baseUrl;
        }

        private string VersionUrl
        {
            get
            {
                return this.baseUrl + VersionApi;
            }
        }

        public Version QueryNewestVersion(string orgName, string platform, string ring)
        {
            Dictionary<string, string> queryParams = new Dictionary<string, string>()
            {
                { "Organization", orgName },
                { "Platform", platform },
                { "Ring", ring },
            };

            string responseString = this.client.GetStringAsync(this.ConstructRequest(this.VersionUrl, queryParams)).GetAwaiter().GetResult();
            VersionResponse versionResponse = VersionResponse.FromJsonString(responseString);

            if (string.IsNullOrEmpty(versionResponse.Version))
            {
                return null;
            }

            return new Version(versionResponse.Version);
        }

        private string ConstructRequest(string baseUrl, Dictionary<string, string> queryParams)
        {
            StringBuilder sb = new StringBuilder(baseUrl);

            if (queryParams.Any())
            {
                sb.Append("?");
            }

            bool isFirst = true;
            foreach (KeyValuePair<string, string> kvp in queryParams)
            {
                if (!isFirst)
                {
                    sb.Append("&");
                }

                isFirst = false;
                sb.Append($"{HttpUtility.UrlEncode(kvp.Key)}={HttpUtility.UrlEncode(kvp.Value)}");
            }

            return sb.ToString();
        }
    }
}
