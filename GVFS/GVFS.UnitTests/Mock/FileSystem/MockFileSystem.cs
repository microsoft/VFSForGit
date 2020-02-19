using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace GVFS.UnitTests.Mock.FileSystem
{
    public class MockFileSystem : PhysicalFileSystem
    {
        public MockFileSystem(MockDirectory rootDirectory)
        {
            this.RootDirectory = rootDirectory;
            this.DeleteNonExistentFileThrowsException = true;
            this.TryCreateOrUpdateDirectoryToAdminModifyPermissionsShouldSucceed = true;
        }

        public MockDirectory RootDirectory { get; private set; }

        public bool DeleteFileThrowsException { get; set; }
        public Exception ExceptionThrownByCreateDirectory { get; set; }

        public bool TryCreateOrUpdateDirectoryToAdminModifyPermissionsShouldSucceed { get; set; }

        /// <summary>
        /// Allow FileMoves without checking the input arguments.
        /// This is to support tests that just want to allow arbitrary
        /// MoveFile calls to succeed.
        /// </summary>
        public bool AllowMoveFile { get; set; }

        /// <summary>
        /// Normal behavior C# File.Delete(..) is to not throw if the file to
        /// be deleted does not exist. However, existing behavior of this mock
        /// is to throw. This flag allows consumers to control this behavior.
        /// </summary>
        public bool DeleteNonExistentFileThrowsException { get; set; }

        public override void DeleteDirectory(string path, bool recursive = true, bool ignoreDirectoryDeleteExceptions = false)
        {
            if (!recursive)
            {
                throw new NotImplementedException();
            }

            this.RootDirectory.DeleteDirectory(path);
        }

        public override bool FileExists(string path)
        {
            return this.RootDirectory.FindFile(path) != null;
        }

        public override bool DirectoryExists(string path)
        {
            return this.RootDirectory.FindDirectory(path) != null;
        }

        public override void CopyFile(string sourcePath, string destinationPath, bool overwrite)
        {
            throw new NotImplementedException();
        }

        public override void DeleteFile(string path)
        {
            if (this.DeleteFileThrowsException)
            {
                throw new IOException("Exception when deleting file");
            }

            MockFile file = this.RootDirectory.FindFile(path);

            if (file == null && !this.DeleteNonExistentFileThrowsException)
            {
                return;
            }

            file.ShouldNotBeNull();

            this.RootDirectory.RemoveFile(path);
        }

        public override void MoveAndOverwriteFile(string sourcePath, string destinationPath)
        {
            if (sourcePath == null || destinationPath == null)
            {
                throw new ArgumentNullException();
            }

            if (this.AllowMoveFile)
            {
                return;
            }

            MockFile sourceFile = this.RootDirectory.FindFile(sourcePath);
            MockFile destinationFile = this.RootDirectory.FindFile(destinationPath);
            if (sourceFile == null)
            {
                throw new FileNotFoundException();
            }

            if (destinationFile != null)
            {
                this.RootDirectory.RemoveFile(destinationPath);
            }

            this.WriteAllText(destinationPath, this.ReadAllText(sourcePath));
            this.RootDirectory.RemoveFile(sourcePath);
        }

        public override bool TryGetNormalizedPath(string path, out string normalizedPath, out string errorMessage)
        {
            normalizedPath = path;
            errorMessage = null;
            return true;
        }

        public override Stream OpenFileStream(string path, FileMode fileMode, FileAccess fileAccess, FileShare shareMode, FileOptions options, bool flushesToDisk)
        {
            MockFile file = this.RootDirectory.FindFile(path);
            if (fileMode == FileMode.Create)
            {
                if (file != null)
                {
                    this.RootDirectory.RemoveFile(path);
                }

                return this.CreateAndOpenFileStream(path);
            }

            if (fileMode == FileMode.OpenOrCreate)
            {
                if (file == null)
                {
                    return this.CreateAndOpenFileStream(path);
                }
            }
            else
            {
                file.ShouldNotBeNull();
            }

            return file.GetContentStream();
        }

        public override void FlushFileBuffers(string path)
        {
            throw new NotImplementedException();
        }

        public override void WriteAllText(string path, string contents)
        {
            MockFile file = new MockFile(path, contents);
            this.RootDirectory.AddOrOverwriteFile(file, path);
        }

        public override string ReadAllText(string path)
        {
            MockFile file = this.RootDirectory.FindFile(path);

            using (StreamReader reader = new StreamReader(file.GetContentStream()))
            {
                return reader.ReadToEnd();
            }
        }

        public override byte[] ReadAllBytes(string path)
        {
            MockFile file = this.RootDirectory.FindFile(path);

            using (Stream s = file.GetContentStream())
            {
                int count = (int)s.Length;

                int pos = 0;
                byte[] result = new byte[count];
                while (count > 0)
                {
                    int n = s.Read(result, pos, count);
                    if (n == 0)
                    {
                        throw new IOException("Unexpected end of stream");
                    }

                    pos += n;
                    count -= n;
                }

                return result;
            }
        }

        public override IEnumerable<string> ReadLines(string path)
        {
            MockFile file = this.RootDirectory.FindFile(path);
            using (StreamReader reader = new StreamReader(file.GetContentStream()))
            {
                while (!reader.EndOfStream)
                {
                    yield return reader.ReadLine();
                }
            }
        }

        public override void CreateDirectory(string path)
        {
            if (this.ExceptionThrownByCreateDirectory != null)
            {
                throw this.ExceptionThrownByCreateDirectory;
            }

            this.RootDirectory.CreateDirectory(path);
        }

        public override bool TryCreateDirectoryWithAdminAndUserModifyPermissions(string directoryPath, out string error)
        {
            throw new NotImplementedException();
        }

        public override bool TryCreateOrUpdateDirectoryToAdminModifyPermissions(ITracer tracer, string directoryPath, out string error)
        {
            error = null;

            if (this.TryCreateOrUpdateDirectoryToAdminModifyPermissionsShouldSucceed)
            {
                // TryCreateOrUpdateDirectoryToAdminModifyPermissions is typically called for paths in C:\ProgramData\GVFS,
                // if it's called for one of those paths remap the paths to be inside the mock: root
                string mockDirectoryPath = directoryPath;
                string gvfsProgramData = @"C:\ProgramData\GVFS";
                if (directoryPath.StartsWith(gvfsProgramData, GVFSPlatform.Instance.Constants.PathComparison))
                {
                    mockDirectoryPath = mockDirectoryPath.Substring(gvfsProgramData.Length);
                    mockDirectoryPath = "mock:" + mockDirectoryPath;
                }

                this.RootDirectory.CreateDirectory(mockDirectoryPath);
                return true;
            }

            return false;
        }

        public override IEnumerable<DirectoryItemInfo> ItemsInDirectory(string path)
        {
            MockDirectory directory = this.RootDirectory.FindDirectory(path);
            directory.ShouldNotBeNull();

            foreach (MockDirectory subDirectory in directory.Directories.Values)
            {
                yield return new DirectoryItemInfo()
                {
                    Name = subDirectory.Name,
                    FullName = subDirectory.FullName,
                    IsDirectory = true
                };
            }

            foreach (MockFile file in directory.Files.Values)
            {
                yield return new DirectoryItemInfo()
                {
                    FullName = file.FullName,
                    Name = file.Name,
                    IsDirectory = false,
                    Length = file.FileProperties.Length
                };
            }
        }

        public override IEnumerable<string> EnumerateDirectories(string path)
        {
            MockDirectory directory = this.RootDirectory.FindDirectory(path);
            directory.ShouldNotBeNull();

            if (directory != null)
            {
                foreach (MockDirectory subDirectory in directory.Directories.Values)
                {
                    yield return subDirectory.FullName;
                }
            }
        }

        public override FileProperties GetFileProperties(string path)
        {
            MockFile file = this.RootDirectory.FindFile(path);
            if (file != null)
            {
                return file.FileProperties;
            }
            else
            {
                return FileProperties.DefaultFile;
            }
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
            if (this.AllowMoveFile)
            {
                return;
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        public override string[] GetFiles(string directoryPath, string mask)
        {
            if (!mask.Equals("*"))
            {
                throw new NotImplementedException();
            }

            MockDirectory directory = this.RootDirectory.FindDirectory(directoryPath);
            directory.ShouldNotBeNull();

            List<string> files = new List<string>();
            foreach (MockFile file in directory.Files.Values)
            {
                files.Add(file.FullName);
            }

            return files.ToArray();
        }

        public override FileVersionInfo GetVersionInfo(string path)
        {
            throw new NotImplementedException();
        }

        public override bool FileVersionsMatch(FileVersionInfo versionInfo1, FileVersionInfo versionInfo2)
        {
            throw new NotImplementedException();
        }

        public override bool ProductVersionsMatch(FileVersionInfo versionInfo1, FileVersionInfo versionInfo2)
        {
            throw new NotImplementedException();
        }

        private Stream CreateAndOpenFileStream(string path)
        {
            MockFile file = this.RootDirectory.CreateFile(path);
            file.ShouldNotBeNull();

            return this.OpenFileStream(path, FileMode.Open, (FileAccess)NativeMethods.FileAccess.FILE_GENERIC_READ, FileShare.Read, callFlushFileBuffers: false);
        }
    }
}
