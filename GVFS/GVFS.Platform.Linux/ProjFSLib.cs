using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using System;
using System.IO;

namespace GVFS.Platform.Linux
{
    public class ProjFSLib : IKernelDriver
    {
        public bool EnumerationExpandsDirectories { get; } = true;
        public bool EmptyPlaceholdersRequireFileSize { get; } = true;

        /* TODO(Linux): check for kernel fuse, libfuse v3, libprojfs;
         *              flesh out all methods below
         */

        public string LogsFolderPath
        {
            get
            {
                return Path.Combine(System.IO.Path.GetTempPath(), "ProjFSLib");
            }
        }

        public bool IsGVFSUpgradeSupported()
        {
            return false;
        }

        public bool IsSupported(string normalizedEnlistmentRootPath, out string warning, out string error)
        {
            warning = null;
            error = null;
            return true;
        }

        public bool TryFlushLogs(out string error)
        {
            Directory.CreateDirectory(this.LogsFolderPath);
            error = string.Empty;
            return true;
        }

        public bool IsReady(JsonTracer tracer, string enlistmentRoot, TextWriter output, out string error)
        {
            error = null;
            return true;
        }

        public bool RegisterForOfflineIO()
        {
            return true;
        }

        public bool UnregisterForOfflineIO()
        {
            return true;
        }

        public bool TryPrepareFolderForCallbacks(string folderPath, out string error, out Exception exception)
        {
            error = null;
            exception = null;
            return true;
        }
    }
}
