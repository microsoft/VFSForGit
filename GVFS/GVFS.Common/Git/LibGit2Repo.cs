using GVFS.Common.Tracing;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace GVFS.Common.Git
{
    public class LibGit2Repo : IDisposable
    {
        private bool disposedValue = false;

        public LibGit2Repo(ITracer tracer, string repoPath)
        {
            this.Tracer = tracer;

            Native.Init();

            IntPtr repoHandle;
            if (Native.Repo.Open(out repoHandle, repoPath) != Native.SuccessCode)
            {
                string reason = Native.GetLastError();
                string message = "Couldn't open repo at " + repoPath + ": " + reason;
                tracer.RelatedWarning(message);

                Native.Shutdown();
                throw new InvalidDataException(message);
            }

            this.RepoHandle = repoHandle;
        }

        protected LibGit2Repo()
        {
        }

        ~LibGit2Repo()
        {
            this.Dispose(false);
        }

        protected ITracer Tracer { get; }
        protected IntPtr RepoHandle { get; private set; }

        public Native.ObjectTypes? GetObjectType(string sha)
        {
            IntPtr objHandle;
            if (Native.RevParseSingle(out objHandle, this.RepoHandle, sha) != Native.SuccessCode)
            {
                return null;
            }

            try
            {
                return Native.Object.GetType(objHandle);
            }
            finally
            {
                Native.Object.Free(objHandle);
            }
        }

        public virtual string GetTreeSha(string commitish)
        {
            IntPtr objHandle;
            if (Native.RevParseSingle(out objHandle, this.RepoHandle, commitish) != Native.SuccessCode)
            {
                return null;
            }

            try
            {
                switch (Native.Object.GetType(objHandle))
                {
                    case Native.ObjectTypes.Commit:
                        GitOid output = Native.IntPtrToGitOid(Native.Commit.GetTreeId(objHandle));
                        return output.ToString();
                }
            }
            finally
            {
                Native.Object.Free(objHandle);
            }

            return null;
        }

        public virtual bool CommitAndRootTreeExists(string commitish, out string treeSha)
        {
            treeSha = this.GetTreeSha(commitish);
            if (treeSha == null)
            {
                return false;
            }

            return this.ObjectExists(treeSha.ToString());
        }

        public virtual bool ObjectExists(string sha)
        {
            IntPtr objHandle;
            if (Native.RevParseSingle(out objHandle, this.RepoHandle, sha) != Native.SuccessCode)
            {
                return false;
            }

            Native.Object.Free(objHandle);
            return true;
        }

        public virtual bool TryCopyBlob(string sha, Action<Stream, long> writeAction)
        {
            IntPtr objHandle;
            if (Native.RevParseSingle(out objHandle, this.RepoHandle, sha) != Native.SuccessCode)
            {
                return false;
            }

            try
            {
                unsafe
                {
                    switch (Native.Object.GetType(objHandle))
                    {
                        case Native.ObjectTypes.Blob:
                            byte* originalData = Native.Blob.GetRawContent(objHandle);
                            long originalSize = Native.Blob.GetRawSize(objHandle);

                            // TODO 938696: UnmanagedMemoryStream marshals content even for CopyTo
                            // If GetRawContent changed to return IntPtr and ProjFS changed WriteBuffer to expose an IntPtr,
                            // We could probably pinvoke memcpy and avoid marshalling.
                            using (Stream mem = new UnmanagedMemoryStream(originalData, originalSize))
                            {
                                writeAction(mem, originalSize);
                            }

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

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposedValue)
            {
                Native.Repo.Free(this.RepoHandle);
                Native.Shutdown();
                this.disposedValue = true;
            }
        }

        public static class Native
        {
            public const uint SuccessCode = 0;

            public const string Git2NativeLibName = GVFSConstants.LibGit2LibraryName;

            public enum ObjectTypes
            {
                Commit = 1,
                Tree = 2,
                Blob = 3,
            }

            public static GitOid IntPtrToGitOid(IntPtr oidPtr)
            {
                return Marshal.PtrToStructure<GitOid>(oidPtr);
            }

            [DllImport(Git2NativeLibName, EntryPoint = "git_libgit2_init")]
            public static extern void Init();

            [DllImport(Git2NativeLibName, EntryPoint = "git_libgit2_shutdown")]
            public static extern int Shutdown();

            [DllImport(Git2NativeLibName, EntryPoint = "git_revparse_single")]
            public static extern uint RevParseSingle(out IntPtr objectHandle, IntPtr repoHandle, string oid);

            public static string GetLastError()
            {
                IntPtr ptr = GetLastGitError();
                if (ptr == IntPtr.Zero)
                {
                    return "Operation was successful";
                }

                return Marshal.PtrToStructure<GitError>(ptr).Message;
            }

            [DllImport(Git2NativeLibName, EntryPoint = "giterr_last")]
            private static extern IntPtr GetLastGitError();

            [StructLayout(LayoutKind.Sequential)]
            private struct GitError
            {
                [MarshalAs(UnmanagedType.LPStr)]
                public string Message;

                public int Klass;
            }

            public static class Repo
            {
                [DllImport(Git2NativeLibName, EntryPoint = "git_repository_open")]
                public static extern uint Open(out IntPtr repoHandle, string path);

                [DllImport(Git2NativeLibName, EntryPoint = "git_repository_free")]
                public static extern void Free(IntPtr repoHandle);
            }

            public static class Object
            {
                [DllImport(Git2NativeLibName, EntryPoint = "git_object_type")]
                public static extern ObjectTypes GetType(IntPtr objectHandle);

                [DllImport(Git2NativeLibName, EntryPoint = "git_object_free")]
                public static extern void Free(IntPtr objHandle);
            }

            public static class Commit
            {
                /// <returns>A handle to an oid owned by LibGit2</returns>
                [DllImport(Git2NativeLibName, EntryPoint = "git_commit_tree_id")]
                public static extern IntPtr GetTreeId(IntPtr commitHandle);
            }

            public static class Blob
            {
                [DllImport(Git2NativeLibName, EntryPoint = "git_blob_rawsize")]
                [return: MarshalAs(UnmanagedType.U8)]
                public static extern long GetRawSize(IntPtr objectHandle);

                [DllImport(Git2NativeLibName, EntryPoint = "git_blob_rawcontent")]
                public static unsafe extern byte* GetRawContent(IntPtr objectHandle);
            }
        }
    }
}