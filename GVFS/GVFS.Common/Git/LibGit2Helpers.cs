using System;
using System.Runtime.InteropServices;

namespace GVFS.Common.Git
{
    public static class LibGit2Helpers
    {
        public const uint SuccessCode = 0;

        public const string Git2DllName = "git2.dll";

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

        [DllImport(Git2DllName, EntryPoint = "git_libgit2_init")]
        public static extern void Init();

        [DllImport(Git2DllName, EntryPoint = "git_libgit2_shutdown")]
        public static extern int Shutdown();
        
        [DllImport(Git2DllName, EntryPoint = "git_revparse_single")]
        public static extern uint RevParseSingle(out IntPtr objectHandle, IntPtr repoHandle, string oid);
        
        [DllImport(Git2DllName, EntryPoint = "git_oid_fromstr")]
        public static extern void OidFromString(ref GitOid oid, string hash);
        
        public static string GetLastError()
        {
            IntPtr ptr = GetLastGitError();
            if (ptr == IntPtr.Zero)
            {
                return "Operation was successful";
            }

            return Marshal.PtrToStructure<GitError>(ptr).Message;
        }

        [DllImport(Git2DllName, EntryPoint = "giterr_last")]
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
            [DllImport(Git2DllName, EntryPoint = "git_repository_open")]
            public static extern uint Open(out IntPtr repoHandle, string path);
                        
            [DllImport(Git2DllName, EntryPoint = "git_tree_free")]
            public static extern void Free(IntPtr repoHandle);
        }

        public static class Object
        {
            [DllImport(Git2DllName, EntryPoint = "git_object_type")]
            public static extern ObjectTypes GetType(IntPtr objectHandle);

            [DllImport(Git2DllName, EntryPoint = "git_object_free")]
            public static extern void Free(IntPtr objHandle);
        }

        public static class Commit
        {
            /// <returns>A handle to an oid owned by LibGit2</returns>
            [DllImport(Git2DllName, EntryPoint = "git_commit_tree_id")]
            public static extern IntPtr GetTreeId(IntPtr commitHandle);
        }

        public static class Blob
        {
            [DllImport(Git2DllName, EntryPoint = "git_blob_rawsize")]
            [return: MarshalAs(UnmanagedType.U8)]
            public static extern long GetRawSize(IntPtr objectHandle);

            [DllImport(Git2DllName, EntryPoint = "git_blob_rawcontent")]
            public static unsafe extern byte* GetRawContent(IntPtr objectHandle);
        }
    }
}