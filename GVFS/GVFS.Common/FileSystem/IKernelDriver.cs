using GVFS.Common.Tracing;

namespace GVFS.Common.FileSystem
{
    public interface IKernelDriver
    {
        string DriverLogFolderName { get; }

        bool IsSupported(string normalizedEnlistmentRootPath, out string warning, out string error);
        string FlushDriverLogs();
        bool TryPrepareFolderForCallbacks(string folderPath, out string error);
        bool IsReady(JsonTracer tracer, string enlistmentRoot, out string error);
    }
}
