using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Platform.Mac;
using GVFS.Virtualization.FileSystem;

namespace GVFS.PlatformLoader
{
    public static partial class GVFSPlatformLoader
    {
        public static FileSystemVirtualizer CreateFileSystemVirtualizer(GVFSContext context, GVFSGitObjects gitObjects)
        {
            return new MacFileSystemVirtualizer(context, gitObjects);
        }
    }
}