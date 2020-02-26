using GVFS.Common.FileSystem;
using System;
using System.IO;

namespace GVFS.UnitTests.Mock.FileSystem
{
    public class MockFileSystemWithCallbacks : PhysicalFileSystem
    {
        public Func<bool> OnFileExists { get; set; }

        public Func<string, FileMode, FileAccess, Stream> OnOpenFileStream { get; set; }

        public override FileProperties GetFileProperties(string path)
        {
            throw new InvalidOperationException("GetFileProperties has not been implemented.");
        }

        public override bool FileExists(string path)
        {
            if (this.OnFileExists == null)
            {
                throw new InvalidOperationException("OnFileExists should be set if it is expected to be called.");
            }

            return this.OnFileExists();
        }

        public override Stream OpenFileStream(string path, FileMode fileMode, FileAccess fileAccess, FileShare shareMode, FileOptions options, bool flushesToDisk)
        {
            if (this.OnOpenFileStream == null)
            {
                throw new InvalidOperationException("OnOpenFileStream should be set if it is expected to be called.");
            }

            return this.OnOpenFileStream(path, fileMode, fileAccess);
        }

        public override void WriteAllText(string path, string contents)
        {
        }

        public override string ReadAllText(string path)
        {
            throw new InvalidOperationException("ReadAllText has not been implemented.");
        }

        public override void DeleteFile(string path)
        {
        }

        public override void DeleteDirectory(string path, bool recursive = true, bool ignoreDirectoryDeleteExceptions = false)
        {
            throw new InvalidOperationException("DeleteDirectory has not been implemented.");
        }

        public override void CreateDirectory(string path)
        {
        }

        public override FileAttributes GetAttributes(string path)
        {
            return FileAttributes.Normal;
        }

        public override void SetAttributes(string path, FileAttributes fileAttributes)
        {
        }

        public override void MoveFile(string sourcePath, string targetPath)
        {
            throw new NotImplementedException();
        }

        public override string[] GetFiles(string directoryPath, string mask)
        {
            throw new NotImplementedException();
        }
    }
}
