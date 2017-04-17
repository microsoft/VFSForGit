using System;
using System.IO;
using System.Linq;

namespace GVFS.Common
{
    public static class EnlistmentUtils
    {
        public static string GetEnlistmentRoot(string directory)
        {
            directory = directory.TrimEnd(GVFSConstants.PathSeparator);
            DirectoryInfo dirInfo;

            try
            {
                dirInfo = new DirectoryInfo(directory);
            }
            catch (Exception)
            {
                return null;
            }

            while (dirInfo != null)
            {
                if (dirInfo.Exists)
                {
                    DirectoryInfo[] dotGvfsDirs = dirInfo.GetDirectories(GVFSConstants.DotGVFSPath);

                    if (dotGvfsDirs.Count() == 1)
                    {
                        return dirInfo.FullName;
                    }
                }

                dirInfo = dirInfo.Parent;
            }

            return null;
        }

        public static string GetNamedPipeName(string enlistmentRoot)
        {
            return string.Format("GVFS_{0}", enlistmentRoot).ToUpper().Replace(':', '_');
        }
    }
}
