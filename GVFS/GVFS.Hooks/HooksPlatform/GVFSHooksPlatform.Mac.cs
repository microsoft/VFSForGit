using GVFS.Platform.Mac;

namespace GVFS.Hooks.HooksPlatform
{
    public static partial class GVFSHooksPlatform
    {
        public static string GetDataRootForGVFS()
        {
            return MacPlatform.GetDataRootForGVFSImplementation();
        }

        public static bool TryGetGVFSEnlistmentRoot(string directory, out string enlistmentRoot, out string errorMessage)
        {
            return MacPlatform.TryGetGVFSEnlistmentRootImplementation(directory, out enlistmentRoot, out errorMessage);
        }

        public static string GetNamedPipeName(string enlistmentRoot)
        {
            return MacPlatform.GetNamedPipeNameImplementation(enlistmentRoot);
        }
    }
}
