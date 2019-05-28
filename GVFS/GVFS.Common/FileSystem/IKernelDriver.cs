using GVFS.Common.Tracing;
using System;
using System.IO;

namespace GVFS.Common.FileSystem
{
    public interface IKernelDriver
    {
        bool EnumerationExpandsDirectories { get; }
        string LogsFolderPath { get; }
        bool IsSupported(string normalizedEnlistmentRootPath, out string warning, out string error);
        bool TryFlushLogs(out string errors);
        bool TryPrepareFolderForCallbacks(string folderPath, out string error, out Exception exception);
        bool IsReady(JsonTracer tracer, string enlistmentRoot, TextWriter output, out string error);
        bool IsGVFSUpgradeSupported();
        bool RegisterForOfflineIO();
    }
}
