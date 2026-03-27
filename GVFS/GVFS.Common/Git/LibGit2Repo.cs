using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace GVFS.Common.Git
{
    public class LibGit2Repo : IDisposable
    {
        private bool disposedValue = false;

        public delegate void MultiVarConfigCallback(string value);

        public LibGit2Repo(ITracer tracer, string repoPath)
        {
            this.Tracer = tracer;

            InitNative();

            IntPtr repoHandle;
            if (TryOpenRepo(repoPath, out repoHandle) != Native.ResultCode.Success)
            {
                string reason = GetLastNativeError();
                string message = "Couldn't open repo at " + repoPath + ": " + reason;
                tracer.RelatedWarning(message);

                if (!reason.EndsWith(" is not owned by current user")
                    || !CheckSafeDirectoryConfigForCaseSensitivityIssue(tracer, repoPath, out repoHandle))
                {
                    ShutdownNative();
                    throw new InvalidDataException(message);
                }
            }

            this.RepoHandle = repoHandle;
        }

        protected LibGit2Repo()
        {
            this.Tracer = NullTracer.Instance;
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
            if (Native.RevParseSingle(out objHandle, this.RepoHandle, sha) != Native.ResultCode.Success)
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
            if (Native.RevParseSingle(out objHandle, this.RepoHandle, commitish) != Native.ResultCode.Success)
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
            if (Native.RevParseSingle(out objHandle, this.RepoHandle, sha) != Native.ResultCode.Success)
            {
                return false;
            }

            Native.Object.Free(objHandle);
            return true;
        }

        public virtual bool TryCopyBlob(string sha, Action<Stream, long> writeAction)
        {
            IntPtr objHandle;
            if (Native.RevParseSingle(out objHandle, this.RepoHandle, sha) != Native.ResultCode.Success)
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

        /// <summary>
        /// Get the list of missing subtrees for the given treeSha.
        /// </summary>
        /// <param name="treeSha">Tree to look up</param>
        /// <param name="missingSubtrees">SHAs of subtrees of this tree which are not downloaded yet.</param>
        public virtual string[] GetMissingSubTrees(string treeSha)
        {
            List<string> missingSubtreesList = new List<string>();
            IntPtr treeHandle;
            if (Native.RevParseSingle(out treeHandle, this.RepoHandle, treeSha) != Native.ResultCode.Success
                || treeHandle == IntPtr.Zero)
            {
                return Array.Empty<string>();
            }

            try
            {
                if (Native.Object.GetType(treeHandle) != Native.ObjectTypes.Tree)
                {
                    return Array.Empty<string>();
                }

                uint entryCount = Native.Tree.GetEntryCount(treeHandle);
                for (uint i = 0; i < entryCount; i++)
                {
                    if (this.IsMissingSubtree(treeHandle, i, out string entrySha))
                    {
                        missingSubtreesList.Add(entrySha);
                    }
                }
            }
            finally
            {
                Native.Object.Free(treeHandle);
            }

            return missingSubtreesList.ToArray();
        }

        /// <summary>
        /// Get a config value from the repo's git config.
        /// </summary>
        /// <param name="name">Name of the config entry</param>
        /// <returns>The config value, or null if not found.</returns>
        public virtual string GetConfigString(string name)
        {
            IntPtr configHandle;
            if (Native.Config.GetConfig(out configHandle, this.RepoHandle) != Native.ResultCode.Success)
            {
                throw new LibGit2Exception($"Failed to get config handle: {Native.GetLastError()}");
            }
            try
            {
                string value;
                Native.ResultCode resultCode = Native.Config.GetString(out value, configHandle, name);
                if (resultCode == Native.ResultCode.NotFound)
                {
                    return null;
                }
                else if (resultCode != Native.ResultCode.Success)
                {
                    throw new LibGit2Exception($"Failed to get config value for '{name}': {Native.GetLastError()}");
                }

                return value;
            }
            finally
            {
                Native.Config.Free(configHandle);
            }
        }

        public virtual bool? GetConfigBool(string name)
        {
            IntPtr configHandle;
            if (Native.Config.GetConfig(out configHandle, this.RepoHandle) != Native.ResultCode.Success)
            {
                throw new LibGit2Exception($"Failed to get config handle: {Native.GetLastError()}");
            }
            try
            {
                bool value;
                Native.ResultCode resultCode = Native.Config.GetBool(out value, configHandle, name);
                if (resultCode == Native.ResultCode.NotFound)
                {
                    return null;
                }
                else if (resultCode != Native.ResultCode.Success)
                {
                    throw new LibGit2Exception($"Failed to get config value for '{name}': {Native.GetLastError()}");
                }

                return value;
            }
            finally
            {
                Native.Config.Free(configHandle);
            }
        }

        public virtual int? GetConfigInt(string name)
        {
            IntPtr configHandle;
            if (Native.Config.GetConfig(out configHandle, this.RepoHandle) != Native.ResultCode.Success)
            {
                throw new LibGit2Exception($"Failed to get config handle: {Native.GetLastError()}");
            }
            try
            {
                int value;
                Native.ResultCode resultCode = Native.Config.GetInt32(out value, configHandle, name);
                if (resultCode == Native.ResultCode.NotFound)
                {
                    return null;
                }
                else if (resultCode != Native.ResultCode.Success)
                {
                    return null;
                }

                return value;
            }
            finally
            {
                Native.Config.Free(configHandle);
            }
        }

        public void ForEachMultiVarConfig(string key, MultiVarConfigCallback callback)
        {
            if (Native.Config.GetConfig(out IntPtr configHandle, this.RepoHandle) != Native.ResultCode.Success)
            {
                throw new LibGit2Exception($"Failed to get config handle: {Native.GetLastError()}");
            }
            try
            {
                ForEachMultiVarConfig(configHandle, key, callback);
            }
            finally
            {
                Native.Config.Free(configHandle);
            }
        }

        public static void ForEachMultiVarConfigInGlobalAndSystemConfig(string key, MultiVarConfigCallback callback)
        {
            if (Native.Config.GetGlobalAndSystemConfig(out IntPtr configHandle) != Native.ResultCode.Success)
            {
                throw new LibGit2Exception($"Failed to get global and system config handle: {Native.GetLastError()}");
            }
            try
            {
                ForEachMultiVarConfig(configHandle, key, callback);
            }
            finally
            {
                Native.Config.Free(configHandle);
            }
        }

        private static void ForEachMultiVarConfig(IntPtr configHandle, string key, MultiVarConfigCallback callback)
        {
            Native.Config.GitConfigMultivarCallback nativeCallback = (entryPtr, payload) =>
            {
                try
                {
                    var entry = Marshal.PtrToStructure<Native.Config.GitConfigEntry>(entryPtr);
                    callback(entry.GetValue());
                }
                catch (Exception)
                {
                    return Native.ResultCode.Failure;
                }
                return 0;
            };
            if (Native.Config.GetMultivarForeach(
                configHandle,
                key,
                regex:"",
                nativeCallback,
                IntPtr.Zero) != Native.ResultCode.Success)
            {
                throw new LibGit2Exception($"Failed to get multivar config for '{key}': {Native.GetLastError()}");
            }
        }

        /// <summary>
        /// Determine if the given index of a tree is a subtree and if it is missing.
        /// If it is a missing subtree, return the SHA of the subtree.
        /// </summary>
        private bool IsMissingSubtree(IntPtr treeHandle, uint i, out string entrySha)
        {
            entrySha = null;
            IntPtr entryHandle = Native.Tree.GetEntryByIndex(treeHandle, i);
            if (entryHandle == IntPtr.Zero)
            {
                return false;
            }

            var entryMode = Native.Tree.GetEntryFileMode(entryHandle);
            if (entryMode != Native.Tree.TreeEntryFileModeDirectory)
            {
                return false;
            }

            var entryId = Native.Tree.GetEntryId(entryHandle);
            if (entryId == IntPtr.Zero)
            {
                return false;
            }

            var rawEntrySha = Native.IntPtrToGitOid(entryId);
            entrySha = rawEntrySha.ToString();

            if (this.ObjectExists(entrySha))
            {
                return false;
            }
            return true;
            /* Both the entryHandle and the entryId handle are owned by the treeHandle, so we shouldn't free them or it will lead to corruption of the later entries */
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

        /// <summary>
        /// Normalize a path for case-insensitive safe.directory comparison:
        /// replace backslashes with forward slashes, convert to upper-case,
        /// and trim trailing slashes.
        /// </summary>
        internal static string NormalizePathForSafeDirectoryComparison(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            string normalized = path.Replace('\\', '/').ToUpperInvariant();
            return normalized.TrimEnd('/');
        }

        /// <summary>
        /// Retrieve all configured safe.directory values from global and system git config.
        /// Virtual so tests can provide fake entries without touching real config.
        /// </summary>
        protected virtual void GetSafeDirectoryConfigEntries(MultiVarConfigCallback callback)
        {
            ForEachMultiVarConfigInGlobalAndSystemConfig("safe.directory", callback);
        }

        /// <summary>
        /// Try to open a repository at the given path.  Virtual so tests can
        /// avoid the native P/Invoke call.
        /// </summary>
        protected virtual Native.ResultCode TryOpenRepo(string path, out IntPtr repoHandle)
        {
            return Native.Repo.Open(out repoHandle, path);
        }

        protected virtual void InitNative()
        {
            Native.Init();
        }

        protected virtual void ShutdownNative()
        {
            Native.Shutdown();
        }

        protected virtual string GetLastNativeError()
        {
            return Native.GetLastError();
        }

        protected bool CheckSafeDirectoryConfigForCaseSensitivityIssue(ITracer tracer, string repoPath, out IntPtr repoHandle)
        {
            /* Libgit2 has a bug where it is case sensitive for safe.directory (especially the
             * drive letter) when git.exe isn't. Until a fix can be made and propagated, work
             * around it by matching the repo path we request to the configured safe directory.
             *
             * See https://github.com/libgit2/libgit2/issues/7037
             */
            repoHandle = IntPtr.Zero;

            string normalizedRequestedPath = NormalizePathForSafeDirectoryComparison(repoPath);

            string configuredMatchingDirectory = null;
            GetSafeDirectoryConfigEntries((string value) =>
            {
                string normalizedConfiguredPath = NormalizePathForSafeDirectoryComparison(value);
                if (normalizedConfiguredPath == normalizedRequestedPath)
                {
                    configuredMatchingDirectory = value;
                }
            });

            return configuredMatchingDirectory != null && TryOpenRepo(configuredMatchingDirectory, out repoHandle) == Native.ResultCode.Success;
        }

        public static class Native
        {
            public enum ResultCode : int
            {
                Success = 0,
                Failure = -1,
                NotFound = -3,
            }

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
            public static extern ResultCode RevParseSingle(out IntPtr objectHandle, IntPtr repoHandle, string oid);

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
                public static extern ResultCode Open(out IntPtr repoHandle, string path);

                [DllImport(Git2NativeLibName, EntryPoint = "git_repository_free")]
                public static extern void Free(IntPtr repoHandle);
            }

            public static class Config
            {
                [DllImport(Git2NativeLibName, EntryPoint = "git_repository_config")]
                public static extern ResultCode GetConfig(out IntPtr configHandle, IntPtr repoHandle);

                [DllImport(Git2NativeLibName, EntryPoint = "git_config_open_default")]
                public static extern ResultCode GetGlobalAndSystemConfig(out IntPtr configHandle);

                [DllImport(Git2NativeLibName, EntryPoint = "git_config_get_string")]
                public static extern ResultCode GetString(out string value, IntPtr configHandle, string name);

                [DllImport(Git2NativeLibName, EntryPoint = "git_config_get_multivar_foreach")]
                public static extern ResultCode GetMultivarForeach(
                    IntPtr configHandle,
                    string name,
                    string regex,
                    GitConfigMultivarCallback callback,
                    IntPtr payload);

                [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
                public delegate ResultCode GitConfigMultivarCallback(
                    IntPtr entryPtr,
                    IntPtr payload);

                [StructLayout(LayoutKind.Sequential)]
                public struct GitConfigEntry
                {
                    public IntPtr Name;
                    public IntPtr Value;
                    public IntPtr BackendType;
                    public IntPtr OriginPath;
                    public uint IncludeDepth;
                    public int Level;

                    public string GetValue()
                    {
                        return Value != IntPtr.Zero ? MarshalUtf8String(Value) : null;
                    }

                    public string GetName()
                    {
                        return Name != IntPtr.Zero ? MarshalUtf8String(Name) : null;
                    }

                    private static string MarshalUtf8String(IntPtr ptr)
                    {
                        if (ptr == IntPtr.Zero)
                        {
                            return null;
                        }

                        int length = 0;
                        while (Marshal.ReadByte(ptr, length) != 0)
                        {
                            length++;
                        }

                        byte[] buffer = new byte[length];
                        Marshal.Copy(ptr, buffer, 0, length);
                        return System.Text.Encoding.UTF8.GetString(buffer);
                    }
                }

                [DllImport(Git2NativeLibName, EntryPoint = "git_config_get_bool")]
                public static extern ResultCode GetBool(out bool value, IntPtr configHandle, string name);

                [DllImport(Git2NativeLibName, EntryPoint = "git_config_get_int32")]
                public static extern ResultCode GetInt32(out int value, IntPtr configHandle, string name);

                [DllImport(Git2NativeLibName, EntryPoint = "git_config_free")]
                public static extern void Free(IntPtr configHandle);
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

            public static class Tree
            {
                [DllImport(Git2NativeLibName, EntryPoint = "git_tree_entrycount")]
                public static extern uint GetEntryCount(IntPtr treeHandle);

                [DllImport(Git2NativeLibName, EntryPoint = "git_tree_entry_byindex")]
                public static extern IntPtr GetEntryByIndex(IntPtr treeHandle, uint index);

                [DllImport(Git2NativeLibName, EntryPoint = "git_tree_entry_id")]
                public static extern IntPtr GetEntryId(IntPtr entryHandle);

                /* git_tree_entry_type requires the object to exist, so we can't use it to check if
                 * a missing entry is a tree. Instead, we can use the file mode to determine if it is a tree. */
                [DllImport(Git2NativeLibName, EntryPoint = "git_tree_entry_filemode")]
                public static extern uint GetEntryFileMode(IntPtr entryHandle);

                public const uint TreeEntryFileModeDirectory = 0x4000;

            }
        }
    }
}