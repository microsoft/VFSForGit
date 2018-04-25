using System.ComponentModel;
using System.IO;

namespace GVFS.Common
{
    public static partial class Paths
    {
        public static bool TryGetFinalPathRoot(string path, out string pathRoot, out string errorMessage)
        {
            pathRoot = null;
            errorMessage = null;
            string finalPathName = null;
            try
            {
                // The folder might not be on disk yet, walk up the path until we find a folder that's on disk
                string parentPath = path;
                while (!string.IsNullOrWhiteSpace(parentPath) && !Directory.Exists(parentPath))
                {
                    parentPath = Path.GetDirectoryName(parentPath);
                }

                if (string.IsNullOrWhiteSpace(parentPath))
                {
                    errorMessage = "Could not get path root. Specified path does not exist and unable to find ancestor of path on disk";
                    return false;
                }

                finalPathName = NativeMethods.GetFinalPathName(parentPath);
            }
            catch (Win32Exception e)
            {
                errorMessage = "Could not get path root. Failed to determine volume: " + e.Message;
                return false;
            }

            pathRoot = Path.GetPathRoot(finalPathName);
            return true;
        }
    }
}
