using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace GVFS.UnitTests.Mock.Physical.FileSystem
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
