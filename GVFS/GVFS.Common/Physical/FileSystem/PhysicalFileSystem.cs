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

        public virtual IDisposable MonitorChanges(
            string directory, 
            NotifyFilters notifyFilter,
            Action<FileSystemEventArgs> onCreate,
            Action<RenamedEventArgs> onRename,
            Action<FileSystemEventArgs> onDelete)
        {
            FileSystemWatcher watcher = new FileSystemWatcher(directory);
            watcher.IncludeSubdirectories = true;
            watcher.NotifyFilter = notifyFilter;
            watcher.InternalBufferSize = WatcherBufferSize;
            watcher.EnableRaisingEvents = true;
            if (onCreate != null)
            {
                watcher.Created += (sender, args) => onCreate(args);
            }

            if (onRename != null)
            {
                watcher.Renamed += (sender, args) =>
                {
                    // Skip the event if args.Name is null.
                    // Name can be null if the FileSystemWatcher's buffer has an entry for OLD_NAME that is not followed by an
                    // entry for NEW_NAME.  This scenario results in two rename events being fired, the first with a null Name and the
                    // second with a null OldName.
                    if (args.Name != null)
                    {
                        onRename(args);
                    }
                };
            }

            if (onDelete != null)
            {
                watcher.Deleted += (sender, args) => onDelete(args);
            }

            return watcher;
        }
    }
}