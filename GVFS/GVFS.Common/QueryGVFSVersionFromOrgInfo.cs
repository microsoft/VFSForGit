using System;
using System.Net.Http;

namespace GVFS.Common
{
    public class QueryGVFSVersionFromOrgInfo : IQueryGVFSVersion
    {
        private HttpClient httpClient;
        private string orgInfoServerUrl;
        private string orgName;
        private string platform;
        private string ring;

        public QueryGVFSVersionFromOrgInfo(
            HttpClient httpClient,
            string orgInfoServerUrl,
            string orgName,
            string platform,
            string ring)
        {
            this.httpClient = httpClient;
            this.orgInfoServerUrl = orgInfoServerUrl;
            this.orgName = orgName;
            this.platform = platform;
            this.ring = ring;
        }

        public Version QueryVersion()
        {
            OrgInfoApiClient infoServer = new OrgInfoApiClient(this.httpClient, this.orgInfoServerUrl);
            return infoServer.QueryNewestVersion(this.orgName, this.platform, this.ring);
        }
    }
}
