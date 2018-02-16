using Microsoft.Win32.SafeHandles;
using System.Collections.Generic;
using System.IO;

namespace GVFS.Common.FileSystem
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

        public virtual bool DirectoryExists(string path)
        {
            return Directory.Exists(path);
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

        public Stream OpenFileStream(string path, FileMode fileMode, FileAccess fileAccess, FileShare shareMode, bool callFlushFileBuffers)
        {
            return this.OpenFileStream(path, fileMode, fileAccess, shareMode, FileOptions.None, callFlushFileBuffers);
        }

        public virtual void MoveAndOverwriteFile(string sourceFileName, string destinationFilename)
        {
            NativeMethods.MoveFile(
                sourceFileName, 
                destinationFilename, 
                NativeMethods.MoveFileFlags.MoveFileReplaceExisting);
        }

        public virtual Stream OpenFileStream(string path, FileMode fileMode, FileAccess fileAccess, FileShare shareMode, FileOptions options, bool callFlushFileBuffers)
        {
            if (callFlushFileBuffers)
            {
                return new FlushToDiskFileStream(path, fileMode, fileAccess, shareMode, DefaultStreamBufferSize, options);
            }

            return new FileStream(path, fileMode, fileAccess, shareMode, DefaultStreamBufferSize, options);
        }

        public virtual void CreateDirectory(string path)
        {
            Directory.CreateDirectory(path);
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

        public virtual FileAttributes GetAttributes(string path)
        {
            return File.GetAttributes(path);
        }

        public virtual void SetAttributes(string path, FileAttributes fileAttributes)
        {
            File.SetAttributes(path, fileAttributes);
        }

        public virtual void MoveFile(string sourcePath, string targetPath)
        {
            File.Move(sourcePath, targetPath);
        }

        public virtual string[] GetFiles(string directoryPath, string mask)
        {
            return Directory.GetFiles(directoryPath, mask);
        }
    }
}