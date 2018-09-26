using GVFS.Common.Http;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GVFS.Common
{
    public class ServerGVFSConfig
    {
        public IEnumerable<VersionRange> AllowedGVFSClientVersions { get; set; }

        public IEnumerable<CacheServerInfo> CacheServers { get; set; } = Enumerable.Empty<CacheServerInfo>();

        public class VersionRange
        {
            public Version Min { get; set; }
            public Version Max { get; set; }
        }
    }
}