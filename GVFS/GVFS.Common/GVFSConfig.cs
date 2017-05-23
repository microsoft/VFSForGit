using System;
using System.Collections.Generic;

namespace GVFS.Common
{
    public class GVFSConfig
    {
        public IEnumerable<VersionRange> AllowedGVFSClientVersions { get; set; }

        public class VersionRange
        {
            public Version Min { get; set; }
            public Version Max { get; set; }
        }
    }
}
