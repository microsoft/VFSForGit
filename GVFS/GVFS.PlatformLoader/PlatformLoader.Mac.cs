using GVFS.Common;
using GVFS.Platform.Mac;

namespace GVFS.PlatformLoader
{
    public static partial class GVFSPlatformLoader
    {
        public static void Initialize()
        {
            GVFSPlatform.Register(new MacPlatform());
            return;
        }
     }
}