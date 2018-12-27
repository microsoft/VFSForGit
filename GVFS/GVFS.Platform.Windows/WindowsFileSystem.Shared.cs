using GVFS.Common;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;

namespace GVFS.Platform.Windows
{
    public partial class WindowsFileSystem
    {
        public static bool TryGetNormalizedPathImplementation(string path, out string normalizedPath, out string errorMessage)
        {
            normalizedPath = null;
            errorMessage = null;
            try
            {
                // The folder might not be on disk yet, walk up the path until we find a folder that's on disk
                Stack<string> removedPathParts = new Stack<string>();
                string parentPath = path;
                while (!string.IsNullOrWhiteSpace(parentPath) && !Directory.Exists(parentPath))
                {
                    removedPathParts.Push(Path.GetFileName(parentPath));
                    parentPath = Path.GetDirectoryName(parentPath);
                }

                if (string.IsNullOrWhiteSpace(parentPath))
                {
                    errorMessage = "Could not get path root. Specified path does not exist and unable to find ancestor of path on disk";
                    return false;
                }

                normalizedPath = NativeMethods.GetFinalPathName(parentPath);

                // normalizedPath now consists of all parts of the path currently on disk, re-add any parts of the path that were popped off
                while (removedPathParts.Count > 0)
                {
                    normalizedPath = Path.Combine(normalizedPath, removedPathParts.Pop());
                }
            }
            catch (Win32Exception e)
            {
                errorMessage = "Could not get path root. Failed to determine volume: " + e.Message;
                return false;
            }

            return true;
        }
    }
}
