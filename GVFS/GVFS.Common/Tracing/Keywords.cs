using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GVFS.Common.Tracing
{
    public enum Keywords : long
    {
        None = 1 << 0,
        Network = 1 << 1,
        DEPRECATED_DOTGIT_FS = 1 << 2,
        Any = ~0,
        NoAsimovSampling = 0x0000800000000000
    }
}