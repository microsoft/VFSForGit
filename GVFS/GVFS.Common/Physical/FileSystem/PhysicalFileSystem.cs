using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace GVFS.Common.Physical.FileSystem
{
    public class PhysicalFileSystem
    {
        public const int DefaultStreamBufferSize = 8192;

        // https://msdn.microsoft.com/en-us/library/system.io.filesystemwatcher.internalbuffersize(v=vs.110).aspx:
        // Max FileSystemWatcher.InternalBufferSize is 64 KB
        private const int WatcherBufferSize = 64 * 1024;

        public static void RecursiveDelete(string path)
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            DirectoryInfo directory = new DirectoryInfo(path);

            foreach (FileInfo file in directory.GetFiles())
            {
                file.Attributes = FileAttributes.Normal;
                file.Delete();
            }

            foreach (DirectoryInfo subDirectory in directory.GetDirectories())
            {
                RecursiveDelete(subDirectory.FullName);
            }

            directory.Delete();
        }

        public virtual bool FileExists(string path)
        {
            return File.Exists(path);
        }

        public virtual void CopyFile(string sourcePath, string destinationPath, bool overwrite)
        {
            File.Copy(sourcePath, destinationPath, overwrite);
        }

        public virtual void DeleteFile(string path)
        {
            File.Delete(path);
        }

        public virtual string ReadAllText(string path)
        {
            return File.ReadAllText(path);
        }

        public virtual IEnumerable<string> ReadLines(string path)
        {
            return File.ReadLines(path);
        }

        public virtual void WriteAllText(string path, string contents)
        {
            File.WriteAllText(path, contents);
        }

        public virtual Stream OpenFileStream(string path, FileMode fileMode, FileAccess fileAccess, FileShare shareMode)
        {
            return this.OpenFileStream(path, fileMode, fileAccess, NativeMethods.FileAttributes.FILE_ATTRIBUTE_NORMAL, shareMode);
        }

        public virtual Stream OpenFileStream(string path, FileMode fileMode, FileAccess fileAccess, NativeMethods.FileAttributes attributes, FileShare shareMode)
        {
            FileAccess access = fileAccess & FileAccess.ReadWrite;
            return new FileStream((SafeFileHandle)this.OpenFile(path, fileMode, fileAccess, (FileAttributes)attributes, shareMode), access, DefaultStreamBufferSize, true);
        }

        public virtual SafeHandle OpenFile(string path, FileMode fileMode, FileAccess fileAccess, FileAttributes attributes, FileShare shareMode)
        {
            return NativeMethods.OpenFile(path, fileMode, (NativeMethods.FileAccess)fileAccess, shareMode, (NativeMethods.FileAttributes)attributes);
        }

        public virtual void DeleteDirectory(string path, bool recursive = false)
        {
            RecursiveDelete(path);
        }

        /// <summary>
        /// Lock specified directory, so it can't be deleted or renamed by any other process
        /// </summary>
        /// <param name="path">Path to existing directory junction</param>
        public virtual SafeFileHandle LockDirectory(string path)
        {
            return NativeMethods.LockDirectory(path);
        }

        public virtual IEnumerable<DirectoryItemInfo> ItemsInDirectory(string path)
        {
            DirectoryInfo ntfsDirectory = new DirectoryInfo(path);
            foreach (FileSystemInfo ntfsItem in ntfsDirectory.GetFileSystemInfos())
            {
                DirectoryItemInfo itemInfo = new DirectoryItemInfo()
                {
                    FullName = ntfsItem.FullName,
                    Name = ntfsItem.Name,
                    IsDirectory = (ntfsItem.Attributes & FileAttributes.Directory) != 0
                };

                if (!itemInfo.IsDirectory)
                {
                    itemInfo.Length = ((FileInfo)ntfsItem).Length;
                }

                yield return itemInfo;
            }
        }

        public virtual FileProperties GetFileProperties(string path)
        {
            FileInfo entry = new FileInfo(path);
            if (entry.Exists)
            {
                return new FileProperties(
                    entry.Attributes,
                    entry.CreationTimeUtc,
                    entry.LastAccessTimeUtc,
                    entry.LastWriteTimeUtc,
                    entry.Length);
            }
            else
            {
                return FileProperties.DefaultFile;
            }
        }
    }
}