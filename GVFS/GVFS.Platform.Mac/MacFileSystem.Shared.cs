using System;
using System.Collections.Generic;
using System.Text;

namespace GVFS.Platform.Mac
{
    public partial class MacFileSystem
    {
        public static bool TryGetNormalizedPathImplementation(string path, out string normalizedPath, out string errorMessage)
        {
            // TODO(Mac): Properly determine normalized paths (e.g. across links)
            errorMessage = null;
            normalizedPath = path;
            return true;
        }
    }
}
