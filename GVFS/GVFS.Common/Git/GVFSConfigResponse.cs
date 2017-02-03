using System;
using System.Collections.Generic;

namespace GVFS.Common.Git
{
    public class GVFSConfigResponse
    {
        public IEnumerable<VersionRange> AllowedGvfsClientVersions { get; set; }

        public class VersionRange
        {
            public Version Min { get; set; }
            public Version Max { get; set; }
        }
    }
}
