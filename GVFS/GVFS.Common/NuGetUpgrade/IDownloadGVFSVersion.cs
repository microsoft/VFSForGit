using System;

namespace GVFS.Common.NuGetUpgrade
{
    public interface IDownloadGVFSVersion
    {
        void DownloadVersion(Version version);
    }
}
