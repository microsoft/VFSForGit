using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace FastFetch.Git
{
    public static class LibGit2Helpers
    {
        public const string Git2DllName = "git2.dll";

        public enum ObjectTypes
        {
            Commit = 1,
            Tree = 2,
            Blob = 3,
        }

        [DllImport(Git2DllName, EntryPoint = "git_libgit2_init")]
        public static extern void Init();

        [DllImport(Git2DllName, EntryPoint = "git_libgit2_shutdown")]
        public static extern void Shutdown();
        
        [DllImport(Git2DllName, EntryPoint = "git_revparse_single")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RevParseSingle(out IntPtr objectHandle, IntPtr repoHandle, string oid);
        
        public static class Repo
        {
            [DllImport(Git2DllName, EntryPoint = "git_repository_open")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool Open(out IntPtr repoHandle, string path);
                        
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