using RGFS.Common.Http;
using System;
using System.Collections.Generic;

namespace RGFS.Common
{
    public class RGFSConfig
    {
        public IEnumerable<VersionRange> AllowedRGFSClientVersions { get; set; }

        public IEnumerable<CacheServerInfo> CacheServers { get; set; }

        public class VersionRange
        {
            public Version Min { get; set; }
            public Version Max { get; set; }
        }
    }
}
