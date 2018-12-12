using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Platform.Windows;
using GVFS.Virtualization.FileSystem;

namespace GVFS.PlatformLoader
{
    public static class GVFSPlatformLoader
    {
        public static FileSystemVirtualizer CreateFileSystemVirtualizer(GVFSContext context, GVFSGitObjects gitObjects)
        {
            return new WindowsFileSystemVirtualizer(context, gitObjects);
        }

        public static void Initialize()
        {
            GVFSPlatform.Register(new WindowsPlatform());
            return;
        }
     }
}