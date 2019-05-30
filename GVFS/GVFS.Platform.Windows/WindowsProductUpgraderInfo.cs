using GVFS.Common;

namespace GVFS.Platform.Windows
{
    public class WindowsProductUpgraderInfo : ProductUpgraderInfoImpl
    {
        public override string GetUpgradeApplicationDirectory()
        {
            return GVFSPlatform.Instance.GetDataRootForGVFSComponent(ProductUpgraderInfo.UpgradeDirectoryName);
        }

        public override string GetUpgradeNonProtectedDirectory()
        {
            return GVFSPlatform.Instance.GetDataRootForGVFSComponent(ProductUpgraderInfo.UpgradeDirectoryName);
        }

        public override string GetUpgradeProtectedDirectory()
        {
            return GVFSPlatform.Instance.GetDataRootForGVFSComponent(ProductUpgraderInfo.UpgradeDirectoryName);
        }
    }
}
