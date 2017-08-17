using System;
using System.IO;
using System.Runtime.InteropServices;

namespace GVFS.UnitTests.Mock.FileSystem
{
    /// <summary>
    /// A "SafeHandle" object to represent fake file contents during native file system calls
    /// </summary>
    public class MockSafeHandle : SafeHandle
    {
        public MockSafeHandle(string filePath, Stream fileContents) : base(IntPtr.Zero, false)
        {
            this.FilePath = filePath;
            this.FileContents = fileContents;
        }

        public string FilePath { get; }

        public Stream FileContents { get; }

        public override bool IsInvalid
        {
            get { return false; }
        }

        protected override bool ReleaseHandle()
        {
            this.FileContents.Dispose();
            return true;
        }
    }
}
