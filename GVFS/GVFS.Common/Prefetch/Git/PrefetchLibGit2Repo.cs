using GVFS.Common.Git;
using GVFS.Common.Tracing;
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace GVFS.Common.Prefetch.Git
{
    public class PrefetchLibGit2Repo : LibGit2Repo
    {
        public PrefetchLibGit2Repo(ITracer tracer, string repoPath)
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
                                GVFSPlatform.Instance.FileSystem.WriteFile(this.Tracer, originalData, originalSize, destination);
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
    }
}
