namespace GVFS.Platform.POSIX
{
    public partial class POSIXFileSystem
    {
        public static bool TryGetNormalizedPathImplementation(string path, out string normalizedPath, out string errorMessage)
        {
            // TODO(#1358): Properly determine normalized paths (e.g. across links)
            errorMessage = null;
            normalizedPath = path;
            return true;
        }
    }
}
