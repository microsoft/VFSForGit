namespace GVFS.Platform.Linux
{
    public partial class LinuxFileSystem
    {
        public static bool TryGetNormalizedPathImplementation(string path, out string normalizedPath, out string errorMessage)
        {
            // TODO(Linux): Properly determine normalized paths (e.g. across links)
            errorMessage = null;
            normalizedPath = path;
            return true;
        }
    }
}
