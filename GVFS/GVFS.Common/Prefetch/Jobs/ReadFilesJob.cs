using GVFS.Common.Tracing;
using Microsoft.Win32.SafeHandles;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace GVFS.Common.Prefetch.Jobs
{
    public class ReadFilesJob : Job
    {
        private readonly ConcurrentDictionary<string, HashSet<string>> blobIdToPaths;
        private readonly BlockingCollection<string> availableBlobs;

        private ITracer tracer;
        private int readFileCount;

        public ReadFilesJob(int maxThreads, ConcurrentDictionary<string, HashSet<string>> blobIdToPaths, BlockingCollection<string> availableBlobs, ITracer tracer)
            : base(maxThreads)
        {
            this.blobIdToPaths = blobIdToPaths;
            this.availableBlobs = availableBlobs;

            this.tracer = tracer;
        }

        public int ReadFileCount
        {
            get { return this.readFileCount; }
        }

        protected override void DoWork()
        {
            using (ITracer activity = this.tracer.StartActivity("ReadFiles", EventLevel.Informational))
            {
                int readFilesCurrentThread = 0;
                int failedFilesCurrentThread = 0;

                byte[] buffer = new byte[1];
                string blobId;
                while (this.availableBlobs.TryTake(out blobId, Timeout.Infinite))
                {
                    foreach (string path in this.blobIdToPaths[blobId])
                    {
                        bool succeeded = false;

                        // TODO(Mac): Replace this with a native method as opposed to a slow .NET method
                        if (!GVFSPlatform.Instance.IsUnderConstruction)
                        {
                            using (SafeFileHandle handle = NativeFileReader.Open(path))
                            {
                                if (!handle.IsInvalid)
                                {
                                    succeeded = NativeFileReader.ReadOneByte(handle, buffer);
                                }
                            }
                        }
                        else
                        {
                            using (FileStream filestream = new FileStream(path, FileMode.Open, FileAccess.Read))
                            {
                                int thisByte = filestream.ReadByte();
                                if (thisByte != -1)
                                {
                                    succeeded = true;
                                }
                            }
                        }

                        if (succeeded)
                        {
                            Interlocked.Increment(ref this.readFileCount);
                            readFilesCurrentThread++;
                        }
                        else
                        {
                            activity.RelatedError("Failed to read " + path);

                            failedFilesCurrentThread++;
                            this.HasFailures = true;
                        }
                    }
                }

                activity.Stop(
                    new EventMetadata
                    {
                        { "FilesRead", readFilesCurrentThread },
                        { "Failures", failedFilesCurrentThread },
                    });
            }
        }

        private class NativeFileReader
        {
            private const uint GenericRead = 0x80000000;
            private const uint OpenExisting = 3;

            public static SafeFileHandle Open(string fileName)
            {
                return CreateFile(fileName, GenericRead, (uint)(FileShare.ReadWrite | FileShare.Delete), 0, OpenExisting, 0, 0);
            }

            public static unsafe bool ReadOneByte(SafeFileHandle handle, byte[] buffer)
            {
                int n = 0;
                fixed (byte* p = buffer)
                {
                    return ReadFile(handle, p, 1, &n, 0);
                }
            }

            [DllImport("kernel32", SetLastError = true, ThrowOnUnmappableChar = true, CharSet = CharSet.Unicode)]
            private static extern unsafe SafeFileHandle CreateFile(
                string fileName,                        
                uint desiredAccess,       
                uint shareMode,           
                uint securityAttributes,  
                uint creationDisposition, 
                uint flagsAndAttributes,  
                int hemplateFile);

            [DllImport("kernel32", SetLastError = true)]
            private static extern unsafe bool ReadFile(
                SafeFileHandle file,      
                void* buffer,            
                int numberOfBytesToRead, 
                int* numberOfBytesRead,       
                int overlapped);
        }
    }
}
