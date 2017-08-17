using GVFS.Common.Http;
using System;
using System.Collections.Generic;

namespace GVFS.Common
{
    public class GVFSConfig
    {
        public IEnumerable<VersionRange> AllowedGVFSClientVersions { get; set; }

        public IEnumerable<CacheServerInfo> CacheServers { get; set; }

        public class VersionRange
        {
            public Version Min { get; set; }
            public Version Max { get; set; }
        }
    }
}
