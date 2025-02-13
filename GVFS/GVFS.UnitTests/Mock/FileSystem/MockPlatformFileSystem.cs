﻿using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using System;

namespace GVFS.UnitTests.Mock.FileSystem
{
    public class MockPlatformFileSystem : IPlatformFileSystem
    {
        public bool SupportsFileMode { get; } = true;

        public void FlushFileBuffers(string path)
        {
            throw new NotSupportedException();
        }

        public void MoveAndOverwriteFile(string sourceFileName, string destinationFilename)
        {
            throw new NotSupportedException();
        }

        public void ChangeMode(string path, ushort mode)
        {
            throw new NotSupportedException();
        }

        public bool TryGetNormalizedPath(string path, out string normalizedPath, out string errorMessage)
        {
            errorMessage = null;
            normalizedPath = path;
            return true;
        }

        public void SetDirectoryLastWriteTime(string path, DateTime lastWriteTime, out bool directoryExists)
        {
            throw new NotSupportedException();
        }

        public bool HydrateFile(string fileName, byte[] buffer)
        {
            throw new NotSupportedException();
        }

        public bool IsExecutable(string fileName)
        {
            throw new NotSupportedException();
        }

        public bool IsSocket(string fileName)
        {
            throw new NotSupportedException();
        }

        public bool TryCreateDirectoryAccessibleByAuthUsers(string directoryPath, out string error, ITracer tracer = null)
        {
            throw new NotSupportedException();
        }

        public bool TryCreateDirectoryWithAdminAndUserModifyPermissions(string directoryPath, out string error)
        {
            throw new NotSupportedException();
        }

        public bool TryCreateOrUpdateDirectoryToAdminModifyPermissions(ITracer tracer, string directoryPath, out string error)
        {
            throw new NotSupportedException();
        }

        public bool IsFileSystemSupported(string path, out string error)
        {
            error = null;
            return true;
        }

        public void EnsureDirectoryIsOwnedByCurrentUser(string workingDirectoryRoot)
        {
            throw new NotSupportedException();
        }
    }
}
