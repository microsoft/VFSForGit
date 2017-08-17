using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Tests.Should;
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace GVFS.UnitTests.Mock.FileSystem
{
    /// <summary>
    /// Intentionally stateless mockup of a large physical directory structure.
    /// </summary>
    public class MassiveMockFileSystem : PhysicalFileSystem
    {
        public const int FoldersPerFolder = 10;
        private static Random randy = new Random(0);
        private string rootPath;
        private int maxDepth;

        public MassiveMockFileSystem(string rootPath, int maxDepth)
        {
            this.rootPath = rootPath;
            this.maxDepth = maxDepth;
        }

        public int MaxTreeSize
        {
            get { return Enumerable.Range(0, this.maxDepth + 1).Sum(i => (int)Math.Pow(FoldersPerFolder, i)); }
        }

        public static string RandomPath(int maxDepth)
        {
            string output = string.Empty;
            int depth = randy.Next(1, maxDepth + 1);
            for (int i = 0; i < depth; ++i)
            {
                char letter = (char)randy.Next('a', 'a' + FoldersPerFolder);
                output = Path.Combine(output, letter.ToString());
            }

            return output;
        }

        public override void WriteAllText(string path, string contents)
        {
        }

        public override string ReadAllText(string path)
        {
            return string.Empty;
        }

        public override Stream OpenFileStream(string path, FileMode fileMode, FileAccess fileAccess, FileShare shareMode)
        {
            return this.OpenFileStream(path, fileMode, fileAccess, NativeMethods.FileAttributes.FILE_ATTRIBUTE_NORMAL, shareMode);
        }

        public override Stream OpenFileStream(string path, FileMode fileMode, FileAccess fileAccess, NativeMethods.FileAttributes attributes, FileShare shareMode)
        {
            return new MemoryStream();
        }

        public override bool FileExists(string path)
        {
            return false;
        }

        public override void CreateDirectory(string path)
        {
            throw new NotImplementedException();
        }

        public override void DeleteDirectory(string path, bool recursive = false)
        {
        }

        public override SafeFileHandle LockDirectory(string path)
        {
            return new SafeFileHandle(IntPtr.Zero, false);
        }

        public override IEnumerable<DirectoryItemInfo> ItemsInDirectory(string path)
        {
            path.StartsWith(this.rootPath).ShouldEqual(true);

            if (path.Count(c => c == '\\') <= this.maxDepth)
            {
                for (char c = 'a'; c < 'a' + FoldersPerFolder; ++c)
                {
                    yield return new DirectoryItemInfo
                    {
                        Name = c.ToString(),
                        FullName = Path.Combine(path, c.ToString()),
                        IsDirectory = true,
                        Length = 0
                    };
                }
            }
        }

        public override FileProperties GetFileProperties(string path)
        {
            return new FileProperties(FileAttributes.Directory, DateTime.Now, DateTime.Now, DateTime.Now, 0);
        }

        public override void CopyFile(string sourcePath, string destinationPath, bool overwrite)
        {
            throw new NotImplementedException();
        }

        public override void DeleteFile(string path)
        {
            throw new NotImplementedException();
        }

        public override SafeHandle OpenFile(string path, FileMode fileMode, FileAccess fileAccess, FileAttributes attributes, FileShare shareMode)
        {
            throw new NotImplementedException();
        }

        public override IEnumerable<string> ReadLines(string path)
        {
            throw new NotImplementedException();
        }
    }
}
