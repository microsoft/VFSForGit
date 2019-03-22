using Microsoft.Windows.ProjFS;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace GVFS.UnitTests.Windows.Windows.Mock
{
    public class MockWriteBuffer : IWriteBuffer
    {
        public MockWriteBuffer(long bufferSize, long allignment)
        {
            unsafe
            {
                this.Length = bufferSize - allignment;
                IntPtr memIntPtr = Marshal.AllocHGlobal(unchecked((int)this.Length));
                byte* memBytePtr = (byte*)memIntPtr.ToPointer();
                this.Stream = new UnmanagedMemoryStream(memBytePtr, this.Length, this.Length, FileAccess.Write);
            }
        }

        public IntPtr Pointer => throw new NotImplementedException();

        public UnmanagedMemoryStream Stream
        {
            get;
            set;
        }

        public long Length
        {
            get;
            set;
        }

        public void Dispose()
        {
            this.Stream.Dispose();
            this.Stream = null;
        }
    }
}
