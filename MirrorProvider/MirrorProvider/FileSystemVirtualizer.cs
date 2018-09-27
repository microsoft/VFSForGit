using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MirrorProvider
{
    public abstract class FileSystemVirtualizer
    {
        private Enlistment enlistment;

        public abstract bool TryConvertVirtualizationRoot(string directory, out string error);
        public virtual bool TryStartVirtualizationInstance(Enlistment enlistment, out string error)
        {
            this.enlistment = enlistment;
            error = null;
            return true;
        }

        protected bool DirectoryExists(string relativePath)
        {
            string fullPathInMirror = Path.Combine(this.enlistment.MirrorRoot, relativePath);
            DirectoryInfo dirInfo = new DirectoryInfo(fullPathInMirror);

            return dirInfo.Exists;
        }

        protected bool FileExists(string relativePath)
        {
            string fullPathInMirror = Path.Combine(this.enlistment.MirrorRoot, relativePath);
            FileInfo fileInfo = new FileInfo(fullPathInMirror);

            return fileInfo.Exists;
        }

        protected ProjectedFileInfo GetFileInfo(string relativePath)
        {
            string fullPathInMirror = Path.Combine(this.enlistment.MirrorRoot, relativePath);
            string fullParentPath = Path.GetDirectoryName(fullPathInMirror);
            string fileName = Path.GetFileName(relativePath);

            string actualCaseName;
            if (this.DirectoryExists(fullParentPath, fileName, out actualCaseName))
            {
                return new ProjectedFileInfo(actualCaseName, size: 0, isDirectory: true);
            }
            else if (this.FileExists(fullParentPath, fileName, out actualCaseName))
            {
                return new ProjectedFileInfo(actualCaseName, size: new FileInfo(fullPathInMirror).Length, isDirectory: false);
            }

            return null;
        }

        protected IEnumerable<ProjectedFileInfo> GetChildItems(string relativePath)
        {
            string fullPathInMirror = Path.Combine(this.enlistment.MirrorRoot, relativePath);
            DirectoryInfo dirInfo = new DirectoryInfo(fullPathInMirror);

            if (!dirInfo.Exists)
            {
                yield break;
            }

            foreach (FileInfo file in dirInfo.GetFiles())
            {
                yield return new ProjectedFileInfo(file.Name, file.Length, isDirectory: false);
            }

            foreach (DirectoryInfo subDirectory in dirInfo.GetDirectories())
            {
                yield return new ProjectedFileInfo(subDirectory.Name, size: 0, isDirectory: true);
            }
        }

        protected FileSystemResult HydrateFile(string relativePath, int bufferSize, Func<byte[], uint, bool> tryWriteBytes)
        {
            string fullPathInMirror = Path.Combine(this.enlistment.MirrorRoot, relativePath);
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

        private bool DirectoryExists(string fullParentPath, string directoryName, out string actualCaseName)
        {
            return this.NameExists(Directory.GetDirectories(fullParentPath), directoryName, out actualCaseName);
        }

        private bool FileExists(string fullParentPath, string fileName, out string actualCaseName)
        {
            return this.NameExists(Directory.GetFiles(fullParentPath), fileName, out actualCaseName);
        }

        private bool NameExists(IEnumerable<string> paths, string name, out string actualCaseName)
        {
            actualCaseName = 
                paths
                .Select(path => Path.GetFileName(path))
                .FirstOrDefault(actualName => actualName.Equals(name, StringComparison.OrdinalIgnoreCase));
            return actualCaseName != null;
        }
    }
}
