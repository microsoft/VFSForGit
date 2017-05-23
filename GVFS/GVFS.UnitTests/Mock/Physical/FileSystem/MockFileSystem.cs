using GVFS.Common;
using GVFS.Common.Physical.FileSystem;
using GVFS.Tests.Should;
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace GVFS.UnitTests.Mock.Physical.FileSystem
{
    public class MockFileSystem : PhysicalFileSystem
    {
        public MockFileSystem(MockDirectory rootDirectory)
        {
            this.RootDirectory = rootDirectory;
        }

        public MockDirectory RootDirectory { get; private set; }

        public override bool FileExists(string path)
        {
            return this.RootDirectory.FindFile(path) != null;
        }

        public override void CopyFile(string sourcePath, string destinationPath, bool overwrite)
        {
            throw new NotImplementedException();
        }

        public override void DeleteFile(string path)
        {
            MockFile file = this.RootDirectory.FindFile(path);
            file.ShouldNotBeNull();

            this.RootDirectory.RemoveFile(path);
        }

        public override Stream OpenFileStream(string path, FileMode fileMode, FileAccess fileAccess, FileShare shareMode)
        {
            return this.OpenFileStream(path, fileMode, fileAccess, NativeMethods.FileAttributes.FILE_ATTRIBUTE_NORMAL, shareMode);            
        }

        public override Stream OpenFileStream(string path, FileMode fileMode, FileAccess fileAccess, NativeMethods.FileAttributes attributes, FileShare shareMode)
        {
            MockFile file = this.RootDirectory.FindFile(path);
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

        public override SafeHandle OpenFile(string path, FileMode fileMode, FileAccess fileAccess, FileAttributes attributes, FileShare shareMode)
        {
            if (fileMode == FileMode.Create)
            {
                MockFile newFile = this.RootDirectory.CreateFile(path);
                FileProperties newProperties = new FileProperties(
                    attributes,
                    newFile.FileProperties.CreationTimeUTC,
                    newFile.FileProperties.LastAccessTimeUTC,
                    newFile.FileProperties.LastWriteTimeUTC,
                    newFile.FileProperties.Length);
                newFile.FileProperties = newProperties;
            }

            return new MockSafeHandle(path, this.OpenFileStream(path, fileMode, fileAccess, shareMode));
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

        public override void DeleteDirectory(string path, bool recursive = false)
        {
            MockDirectory directory = this.RootDirectory.FindDirectory(path);
            directory.ShouldNotBeNull();

            this.RootDirectory.DeleteDirectory(path);
        }

        public override SafeFileHandle LockDirectory(string path)
        {
            return new SafeFileHandle(IntPtr.Zero, false);
        }

        public override IEnumerable<DirectoryItemInfo> ItemsInDirectory(string path)
        {
            MockDirectory directory = this.RootDirectory.FindDirectory(path);
            directory.ShouldNotBeNull();

            if (directory != null)
            {
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

        private Stream CreateAndOpenFileStream(string path)
        {
            MockFile file = this.RootDirectory.CreateFile(path);
            file.ShouldNotBeNull();

            return this.OpenFileStream(path, FileMode.Open, (FileAccess)NativeMethods.FileAccess.FILE_GENERIC_READ, FileShare.Read);
        }
    }
}
