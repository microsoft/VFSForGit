using GVFS.Common.Tracing;
using System;

namespace GVFS.Common.FileSystem
{
    public interface IPlatformFileSystem
    {
        bool SupportsFileMode { get; }
        void FlushFileBuffers(string path);
        void MoveAndOverwriteFile(string sourceFileName, string destinationFilename);
        bool TryGetNormalizedPath(string path, out string normalizedPath, out string errorMessage);
        void SetDirectoryLastWriteTime(string path, DateTime lastWriteTime, out bool directoryExists);
        void ChangeMode(string path, ushort mode);
        bool HydrateFile(string fileName, byte[] buffer);
        bool IsExecutable(string filePath);
        bool IsSocket(string filePath);
        bool TryCreateDirectoryAccessibleByAuthUsers(string directoryPath, out string error, ITracer tracer = null);
        bool TryCreateDirectoryWithAdminAndUserModifyPermissions(string directoryPath, out string error);
        bool TryCreateOrUpdateDirectoryToAdminModifyPermissions(ITracer tracer, string directoryPath, out string error);
        bool IsFileSystemSupported(string path, out string error);
        void EnsureDirectoryIsOwnedByCurrentUser(string workingDirectoryRoot);
    }
}
