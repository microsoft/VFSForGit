using GVFS.Platform.Mac;

namespace GVFS.Hooks.HooksPlatform
{
    public static partial class GVFSHooksPlatform
    {
        public static string GetDataRootForGVFS()
        {
            return MacPlatform.GetDataRootForGVFSImplementation();
        }
    }
}
