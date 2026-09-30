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
        /// <summary>
        /// Hydrates a file by reading it, forcing any virtualization layer to materialize its content.
        /// </summary>
        /// <param name="fileName">Path of the file to hydrate.</param>
        /// <param name="buffer">Scratch buffer used to read from the file. Must be at least 1 byte.</param>
        /// <param name="failure">
        /// The exception that caused hydration to fail, or null if hydration succeeded.
        /// </param>
        /// <returns>True if the file was hydrated successfully, false otherwise.</returns>
        bool HydrateFile(string fileName, byte[] buffer, out Exception failure);
        bool IsExecutable(string filePath);
        bool IsSocket(string filePath);
        bool TryCreateDirectoryAccessibleByAuthUsers(string directoryPath, out string error, ITracer tracer = null);
        bool TryCreateDirectoryWithAdminAndUserModifyPermissions(string directoryPath, out string error);
        bool TryCreateOrUpdateDirectoryToAdminModifyPermissions(ITracer tracer, string directoryPath, out string error);
        bool IsFileSystemSupported(string path, out string error);
        void EnsureDirectoryIsOwnedByCurrentUser(string workingDirectoryRoot);
    }
}
