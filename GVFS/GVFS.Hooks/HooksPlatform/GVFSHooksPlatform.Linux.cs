using GVFS.Platform.Linux;

namespace GVFS.Hooks.HooksPlatform
{
    public static partial class GVFSHooksPlatform
    {
        public static string GetDataRootForGVFS()
        {
            return LinuxPlatform.GetDataRootForGVFSImplementation();
        }
    }
}
