using GVFS.Common;
using System.Collections.Generic;

namespace GVFS.UnitTests.Mock.Common
{
    public class MockLocalGVFSConfigBuilder
    {
        private string defaultRing;
        private string defaultUpgradeFeedUrl;
        private string defaultUpgradeFeedPackageName;
        private string defaultOrgServerUrl;

        private Dictionary<string, string> entries;

        public MockLocalGVFSConfigBuilder(
            string defaultRing,
            string defaultUpgradeFeedUrl,
            string defaultUpgradeFeedPackageName,
            string defaultOrgServerUrl)
        {
            this.defaultRing = defaultRing;
            this.defaultUpgradeFeedUrl = defaultUpgradeFeedUrl;
            this.defaultUpgradeFeedPackageName = defaultUpgradeFeedPackageName;
            this.defaultOrgServerUrl = defaultOrgServerUrl;
            this.entries = new Dictionary<string, string>();
        }

        public MockLocalGVFSConfigBuilder WithUpgradeRing(string value = null)
        {
            return this.With(GVFSConstants.LocalGVFSConfig.UpgradeRing, value ?? this.defaultRing);
        }

        public MockLocalGVFSConfigBuilder WithNoUpgradeRing()
        {
            return this.WithNo(GVFSConstants.LocalGVFSConfig.UpgradeRing);
        }

        public MockLocalGVFSConfigBuilder WithUpgradeFeedPackageName(string value = null)
        {
            return this.With(GVFSConstants.LocalGVFSConfig.UpgradeFeedPackageName, value ?? this.defaultUpgradeFeedPackageName);
        }

        public MockLocalGVFSConfigBuilder WithNoUpgradeFeedPackageName()
        {
            return this.WithNo(GVFSConstants.LocalGVFSConfig.UpgradeFeedPackageName);
        }

        public MockLocalGVFSConfigBuilder WithUpgradeFeedUrl(string value = null)
        {
            return this.With(GVFSConstants.LocalGVFSConfig.UpgradeFeedUrl, value ?? this.defaultUpgradeFeedUrl);
        }

        public MockLocalGVFSConfigBuilder WithNoUpgradeFeedUrl()
        {
            return this.WithNo(GVFSConstants.LocalGVFSConfig.UpgradeFeedUrl);
        }

        public MockLocalGVFSConfigBuilder WithOrgInfoServerUrl(string value = null)
        {
            return this.With(GVFSConstants.LocalGVFSConfig.OrgInfoServerUrl, value ?? this.defaultUpgradeFeedUrl);
        }

        public MockLocalGVFSConfigBuilder WithNoOrgInfoServerUrl()
        {
            return this.WithNo(GVFSConstants.LocalGVFSConfig.OrgInfoServerUrl);
        }

        public MockLocalGVFSConfig Build()
        {
            MockLocalGVFSConfig gvfsConfig = new MockLocalGVFSConfig();
            foreach (KeyValuePair<string, string> kvp in this.entries)
            {
                gvfsConfig.TrySetConfig(kvp.Key, kvp.Value, out _);
            }

            return gvfsConfig;
        }

        private MockLocalGVFSConfigBuilder With(string key, string value)
        {
            this.entries.Add(key, value);
            return this;
        }

        private MockLocalGVFSConfigBuilder WithNo(string key)
        {
            this.entries.Remove(key);
            return this;
        }
    }
}
