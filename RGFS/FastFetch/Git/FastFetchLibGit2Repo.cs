using RGFS.Common.Git;
using RGFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace FastFetch.Git
{
    public class FastFetchLibGit2Repo : LibGit2Repo
    {
        private const int AccessDeniedWin32Error = 5;

        public FastFetchLibGit2Repo(ITracer tracer, string repoPath)
            : base(tracer, repoPath)
        {
        }

        public virtual bool TryCopyBlobToFile(string sha, IEnumerable<string> destinations, out long bytesWritten)
        {
            IntPtr objHandle;
            if (Native.RevParseSingle(out objHandle, this.RepoHandle, sha) != Native.SuccessCode)
            {
                bytesWritten = 0;
                EventMetadata metadata = new EventMetadata();
                metadata.Add("ObjectSha", sha);
                this.Tracer.RelatedError(metadata, "Couldn't find object");
                return false;
            }

            try
            {
                // Avoid marshalling raw content by using byte* and native writes
                unsafe
                {
                    switch (Native.Object.GetType(objHandle))
                    {
                        case Native.ObjectTypes.Blob:
                            byte* originalData = Native.Blob.GetRawContent(objHandle);
                            long originalSize = Native.Blob.GetRawSize(objHandle);

                            foreach (string destination in destinations)
                            {
                                try
                                {
                                    using (SafeFileHandle fileHandle = OpenForWrite(this.Tracer, destination))
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
                                            if (!Native.WriteFile(fileHandle, data, toWrite, out written, IntPtr.Zero))
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
                                    this.Tracer.RelatedError("Exception writing {0}: {1}", destination, e);
                                    throw;
                                }
                            }

                            bytesWritten = originalSize * destinations.Count();
                            break;
                        default:
                            throw new NotSupportedException("Copying object types other than blobs is not supported.");
                    }
                }
            }
            finally
            {
                Native.Object.Free(objHandle);
            }

            return true;
        }

        private static SafeFileHandle OpenForWrite(ITracer tracer, string fileName)
        {
            SafeFileHandle handle = Native.CreateFile(fileName, FileAccess.Write, FileShare.None, IntPtr.Zero, FileMode.Create, FileAttributes.Normal, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                // If we get a access denied, try reverting the acls to defaults inherited by parent
                if (Marshal.GetLastWin32Error() == AccessDeniedWin32Error)
                {
                    tracer.RelatedEvent(
                        EventLevel.Warning,
                        "FailedOpenForWrite",
                        new EventMetadata
                        {
                            { TracingConstants.MessageKey.WarningMessage, "Received access denied. Attempting to delete." },
                            { "FileName", fileName }
                        });

                    File.SetAttributes(fileName, FileAttributes.Normal);
                    File.Delete(fileName);

                    handle = Native.CreateFile(fileName, FileAccess.Write, FileShare.None, IntPtr.Zero, FileMode.Create, FileAttributes.Normal, IntPtr.Zero);
                }
            }

            return handle;
        }
    }
}
