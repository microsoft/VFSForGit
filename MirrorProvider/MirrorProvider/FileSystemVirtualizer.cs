using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MirrorProvider
{
    public abstract class FileSystemVirtualizer
    {
        protected Enlistment Enlistment { get; private set; }

        protected abstract StringComparison PathComparison { get; }
        protected abstract StringComparer PathComparer { get; }

        public abstract bool TryConvertVirtualizationRoot(string directory, out string error);
        public virtual bool TryStartVirtualizationInstance(Enlistment enlistment, out string error)
        {
            this.Enlistment = enlistment;
            error = null;
            return true;
        }

        public virtual void Stop()
        {
        }

        protected string GetFullPathInMirror(string relativePath)
        {
            return Path.Combine(this.Enlistment.MirrorRoot, relativePath);
        }

        protected bool DirectoryExists(string relativePath)
        {
            string fullPathInMirror = this.GetFullPathInMirror(relativePath);
            DirectoryInfo dirInfo = new DirectoryInfo(fullPathInMirror);

            return dirInfo.Exists;
        }

        protected bool FileExists(string relativePath)
        {
            string fullPathInMirror = this.GetFullPathInMirror(relativePath);
            FileInfo fileInfo = new FileInfo(fullPathInMirror);

            return fileInfo.Exists;
        }

        protected ProjectedFileInfo GetFileInfo(string relativePath)
        {
            string fullPathInMirror = this.GetFullPathInMirror(relativePath);
            string fullParentPath = Path.GetDirectoryName(fullPathInMirror);
            string fileName = Path.GetFileName(relativePath);

            string actualCaseName;
            ProjectedFileInfo.FileType type;
            if (this.FileOrDirectoryExists(fullParentPath, fileName, out actualCaseName, out type))
            {
                return new ProjectedFileInfo(
                    actualCaseName, 
                    size: (type == ProjectedFileInfo.FileType.File) ? new FileInfo(fullPathInMirror).Length : 0, 
                    type: type);
            }

            return null;
        }

        protected IEnumerable<ProjectedFileInfo> GetChildItems(string relativePath)
        {
            string fullPathInMirror = this.GetFullPathInMirror(relativePath);
            DirectoryInfo dirInfo = new DirectoryInfo(fullPathInMirror);

            if (!dirInfo.Exists)
            {
                yield break;
            }

            foreach (FileSystemInfo fileSystemInfo in dirInfo.GetFileSystemInfos())
            {
                if ((fileSystemInfo.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                {
                    // While not 100% accurate on all platforms, for simplicity assume that if the the file has reparse data it's a symlink
                    yield return new ProjectedFileInfo(
                        fileSystemInfo.Name,
                        size: 0,
                        type: ProjectedFileInfo.FileType.SymLink);
                }
                else if ((fileSystemInfo.Attributes & FileAttributes.Directory) == FileAttributes.Directory)
                {
                    yield return new ProjectedFileInfo(
                        fileSystemInfo.Name,
                        size: 0,
                        type: ProjectedFileInfo.FileType.Directory);
                }
                else
                {
                    FileInfo fileInfo = fileSystemInfo as FileInfo;
                    yield return new ProjectedFileInfo(
                        fileInfo.Name,
                        fileInfo.Length,
                        ProjectedFileInfo.FileType.File);
                }

            }
        }

        protected FileSystemResult HydrateFile(string relativePath, int bufferSize, Func<byte[], uint, bool> tryWriteBytes)
        {
            string fullPathInMirror = this.GetFullPathInMirror(relativePath);
            if (!File.Exists(fullPathInMirror))
            {
                return FileSystemResult.EFileNotFound;
            }

            using (FileStream fs = new FileStream(fullPathInMirror, FileMode.Open, FileAccess.Read))
            {
                long remainingData = fs.Length;
                byte[] buffer = new byte[bufferSize];

                while (remainingData > 0)
                {
                    int bytesToCopy = (int)Math.Min(remainingData, buffer.Length);
                    if (fs.Read(buffer, 0, bytesToCopy) != bytesToCopy)
                    {
                        return FileSystemResult.EIOError;
                    }

                    if (!tryWriteBytes(buffer, (uint)bytesToCopy))
                    {
                        return FileSystemResult.EIOError;
                    }

                    remainingData -= bytesToCopy;
                }
            }

            return FileSystemResult.Success;
        }

        private bool FileOrDirectoryExists(
            string fullParentPath,
            string fileName,
            out string actualCaseName,
            out ProjectedFileInfo.FileType type)
        {
            actualCaseName = null;
            type = ProjectedFileInfo.FileType.Invalid;

            DirectoryInfo dirInfo = new DirectoryInfo(fullParentPath);
            if (!dirInfo.Exists)
            {
                return false;
            }

            FileSystemInfo fileSystemInfo = 
                dirInfo
                .GetFileSystemInfos()
                .FirstOrDefault(fsInfo => fsInfo.Name.Equals(fileName, PathComparison));

            if (fileSystemInfo == null)
            {
                return false;
            }

            actualCaseName = fileSystemInfo.Name;

            if ((fileSystemInfo.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
            {
                type = ProjectedFileInfo.FileType.SymLink;
            }
            else if ((fileSystemInfo.Attributes & FileAttributes.Directory) == FileAttributes.Directory)
            {
                type = ProjectedFileInfo.FileType.Directory;
            }
            else
            {
                type = ProjectedFileInfo.FileType.File;
            }

            return true;
        }
    }
}
