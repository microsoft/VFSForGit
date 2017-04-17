using GVFS.Common.Tracing;
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace FastFetch.Git
{
    public class LibGit2Repo : IDisposable
    {
        private ITracer tracer;
        private IntPtr repoHandle;
        private bool disposedValue = false;

        public LibGit2Repo(ITracer tracer, string repoPath)
        {
            this.tracer = tracer;
            LibGit2Helpers.Init();

            if (LibGit2Helpers.Repo.Open(out this.repoHandle, repoPath))
            {
                throw new InvalidDataException("Couldn't open repo at: " + repoPath);
            }
        }

        ~LibGit2Repo()
        {
            this.Dispose(false);
        }

        public virtual bool TryCopyBlobToFile(string sha, IEnumerable<string> destinations, out long bytesWritten)
        {
            IntPtr objHandle;
            if (LibGit2Helpers.RevParseSingle(out objHandle, this.repoHandle, sha))
            {
                bytesWritten = 0;
                EventMetadata metadata = new EventMetadata();
                metadata.Add("ObjectSha", sha);
                metadata.Add("ErrorMessage", "Couldn't find object");
                this.tracer.RelatedError(metadata);
                return false;
            }

            try
            {
                // Avoid marshalling raw content
                unsafe
                {
                    switch (LibGit2Helpers.Object.GetType(objHandle))
                    {
                        case LibGit2Helpers.ObjectTypes.Blob:
                            byte* originalData = LibGit2Helpers.Blob.GetRawContent(objHandle);
                            long originalSize = LibGit2Helpers.Blob.GetRawSize(objHandle);

                            foreach (string destination in destinations)
                            {
                                try
                                {
                                    using (SafeFileHandle fileHandle = OpenForWrite(destination))
                                    {
                                        if (fileHandle.IsInvalid)
                                        {
                                            throw new Win32Exception(Marshal.GetLastWin32Error());
                                        }

                                        byte* data = originalData;
                                        long size = originalSize;
                                        uint written = 0;
                                        while (size > 0)
                                        {
                                            uint toWrite = size < uint.MaxValue ? (uint)size : uint.MaxValue;
                                            if (!WriteFile(fileHandle, data, toWrite, out written, IntPtr.Zero))
                                            {
                                                throw new Win32Exception(Marshal.GetLastWin32Error());
                                            }

                                            size -= written;
                                            data = data + written;
                                        }
                                    }
                                }
                                catch (Exception e)
                                {
                                    this.tracer.RelatedError("Exception writing {0}: {1}", destination, e);
                                    throw e;
                                }
                            }
                            
                            bytesWritten = originalSize * destinations.Count();
                            break;
                        default:
                            throw new NotImplementedException("FastFetch should not need to support copying any object types except blobs.");
                    }
                }
            }
            finally
            {
                LibGit2Helpers.Object.Free(objHandle);
            }

            return true;
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposedValue)
            {
                LibGit2Helpers.Repo.Free(this.repoHandle);
                LibGit2Helpers.Shutdown();
                this.disposedValue = true;
            }
        }
        
        private static SafeFileHandle OpenForWrite(string fileName)
        {
            return CreateFile(fileName, FileAccess.Write, FileShare.None, IntPtr.Zero, FileMode.Create, FileAttributes.Normal, IntPtr.Zero);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            [MarshalAs(UnmanagedType.LPTStr)] string filename,
            [MarshalAs(UnmanagedType.U4)] FileAccess access,
            [MarshalAs(UnmanagedType.U4)] FileShare share,
            IntPtr securityAttributes, // optional SECURITY_ATTRIBUTES struct or IntPtr.Zero
            [MarshalAs(UnmanagedType.U4)] FileMode creationDisposition,
            [MarshalAs(UnmanagedType.U4)] FileAttributes flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static unsafe extern bool WriteFile(SafeFileHandle file, byte* buffer, uint numberOfBytesToWrite, out uint numberOfBytesWritten, IntPtr overlapped);

        private unsafe class FileWrapper
        {
            public FileWrapper(SafeFileHandle handle, byte* data, long dataLength)
            {
            }
        }
    }
}