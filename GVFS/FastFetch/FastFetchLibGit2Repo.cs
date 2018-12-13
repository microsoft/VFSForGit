using GVFS.Common.Git;
using GVFS.Common.Prefetch.Git;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace FastFetch
{
    public class FastFetchLibGit2Repo : LibGit2Repo
    {
        private bool isUnixOS;

        public FastFetchLibGit2Repo(ITracer tracer, string repoPath)
            : base(tracer, repoPath)
        {
            this.isUnixOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        }

        public virtual bool TryCopyBlobToFile(string sha, IEnumerable<PathWithMode> destinations, out long bytesWritten)
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

                            foreach (PathWithMode destination in destinations)
                            {
                                if (!this.isUnixOS)
                                {
                                    NativeWindowsMethods.WriteFile(this.Tracer, originalData, originalSize, destination.Path, destination.Mode);
                                }
                                else
                                {
                                    NativeUnixMethods.WriteFile(this.Tracer, originalData, originalSize, destination.Path, destination.Mode);
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
    }
}
