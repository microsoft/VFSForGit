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
        private IntPtr memIntPtr;
        private bool disposed = false;

        public MockWriteBuffer(long bufferSize)
        {
            unsafe
            {
                this.Length = bufferSize;
                this.memIntPtr = Marshal.AllocHGlobal(unchecked((int)this.Length));
                byte* memBytePtr = (byte*)this.memIntPtr.ToPointer();
                this.Stream = new UnmanagedMemoryStream(memBytePtr, this.Length, this.Length, FileAccess.Write);
            }
        }

        ~MockWriteBuffer()
        {
            this.Dispose(false);
        }

        public IntPtr Pointer => this.memIntPtr;

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
            this.Dispose(true);
            GC.SuppressFinalize(true);
        }

        protected void Dispose(bool disposing)
        {
            if (this.disposed)
            {
                return;
            }

            if (disposing)
            {
                if (this.Stream != null)
                {
                    this.Stream.Dispose();
                    this.Stream = null;
                }
            }

            if (this.memIntPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(this.memIntPtr);
                this.memIntPtr = IntPtr.Zero;
            }

            this.disposed = true;
        }
    }
}
