using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace GVFS.UnitTests.Common
{
    /// <summary>
    /// Tests <see cref="LibGit2Repo.EnumerateTree"/> against a repository in a temporary folder.
    /// The repository-built libgit2 writes the objects, so the tests do not start git.exe.
    /// </summary>
    [TestFixture]
    public class LibGit2TreeEnumerationTests
    {
        // "café.txt", from a \u escape so that the test does not depend on the encoding of this file
        private const string NonAsciiName = "caf\u00e9.txt";

        private const uint BlobMode = 0x81A4;       // 100644
        private const uint ExecutableMode = 0x81ED; // 100755
        private const uint LinkMode = 0xA000;       // 120000
        private const uint CommitMode = 0xE000;     // 160000
        private const uint TreeMode = 0x4000;       // 040000

        private const int OidLength = 20;
        private const int ObjectTypeTree = 2;

        private string repoRoot;
        private byte[] blob;
        private byte[] script;
        private byte[] emptyTree;
        private byte[] subTree;
        private byte[] rootTree;
        private byte[] corruptTree;
        private byte[] submoduleCommit;

        [OneTimeSetUp]
        public void CreateRepo()
        {
            this.repoRoot = Path.Combine(Path.GetTempPath(), "GVFS.LibGit2TreeEnumerationTests_" + Path.GetRandomFileName());

            LibGit2Repo.Native.Init();
            try
            {
                Check(Native.InitRepository(out IntPtr repo, this.repoRoot, isBare: 0), "git_repository_init");
                try
                {
                    this.blob = WriteBlob(repo, "hello\n");
                    this.script = WriteBlob(repo, "#!/bin/sh\n");
                    this.submoduleCommit = Enumerable.Repeat((byte)0x11, OidLength).ToArray();

                    this.emptyTree = WriteTree(repo);
                    this.subTree = WriteTree(repo, ("b.txt", this.blob, BlobMode));

                    // The tree builder sorts the entries, so the insert order does not matter.
                    this.rootTree = WriteTree(
                        repo,
                        ("sub", this.subTree, TreeMode),
                        ("sub.txt", this.blob, BlobMode),
                        ("run.sh", this.script, ExecutableMode),
                        ("module", this.submoduleCommit, CommitMode),
                        ("link", this.blob, LinkMode),
                        ("empty", this.emptyTree, TreeMode),
                        (NonAsciiName, this.blob, BlobMode),
                        ("a.txt", this.blob, BlobMode));

                    // An entry without the NUL after its name and without an object id
                    this.corruptTree = WriteRawObject(repo, Encoding.ASCII.GetBytes("100644 a.txt"), ObjectTypeTree);
                }
                finally
                {
                    LibGit2Repo.Native.Repo.Free(repo);
                }
            }
            finally
            {
                LibGit2Repo.Native.Shutdown();
            }
        }

        [OneTimeTearDown]
        public void DeleteRepo()
        {
            if (this.repoRoot != null)
            {
                // libgit2 makes loose objects read-only. DeleteDirectory clears that attribute.
                new PhysicalFileSystem().DeleteDirectory(this.repoRoot);
            }
        }

        [TestCase]
        public void EnumerateTreeReturnsEveryEntryInTreeOrder()
        {
            List<string> actual = new List<string>();
            using (LibGit2Repo repo = new LibGit2Repo(new MockTracer(), this.repoRoot))
            {
                repo.EnumerateTree(this.rootTree, (name, objectId, fileMode) =>
                {
                    actual.Add(Describe(Encoding.UTF8.GetString(name), objectId.ToArray(), fileMode));
                }).ShouldEqual(TreeEnumerationResult.Success);
            }

            // Git sorts a tree entry as if its name ends with '/', so "sub.txt" comes before "sub".
            actual.ShouldMatchInOrder(
                Describe("a.txt", this.blob, BlobMode),
                Describe(NonAsciiName, this.blob, BlobMode),
                Describe("empty", this.emptyTree, TreeMode),
                Describe("link", this.blob, LinkMode),
                Describe("module", this.submoduleCommit, CommitMode),
                Describe("run.sh", this.script, ExecutableMode),
                Describe("sub.txt", this.blob, BlobMode),
                Describe("sub", this.subTree, TreeMode));
        }

        [TestCase]
        public void EnumerateTreeReportsMissingTreeAndNonTree()
        {
            using (LibGit2Repo repo = new LibGit2Repo(new MockTracer(), this.repoRoot))
            {
                int calls = 0;
                TreeEntryVisitor visitor = (name, objectId, fileMode) => calls++;

                repo.EnumerateTree(Enumerable.Repeat((byte)0x22, OidLength).ToArray(), visitor).ShouldEqual(TreeEnumerationResult.MissingTree);
                repo.EnumerateTree(this.blob, visitor).ShouldEqual(TreeEnumerationResult.NotTree);
                repo.EnumerateTree(this.emptyTree, visitor).ShouldEqual(TreeEnumerationResult.Success);
                calls.ShouldEqual(0);
            }
        }

        [TestCase]
        public void EnumerateTreeReportsCorruptTreeAndLogsTheNativeError()
        {
            MockTracer tracer = new MockTracer();
            using (LibGit2Repo repo = new LibGit2Repo(tracer, this.repoRoot))
            {
                int calls = 0;
                repo.EnumerateTree(this.corruptTree, (name, objectId, fileMode) => calls++).ShouldEqual(TreeEnumerationResult.CorruptTree);
                calls.ShouldEqual(0);
            }

            tracer.RelatedWarningEvents.ShouldContain(message => message.Contains("git_object_lookup failed"));
        }

        private static string Describe(string name, byte[] objectId, uint fileMode)
        {
            return Convert.ToString(fileMode, 8).PadLeft(6, '0') + " " + SHA1Util.HexStringFromBytes(objectId) + " " + name;
        }

        private static byte[] WriteBlob(IntPtr repo, string content)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(content);
            byte[] id = new byte[OidLength];
            Check(Native.CreateBlobFromBuffer(id, repo, bytes, (UIntPtr)bytes.Length), "git_blob_create_from_buffer");
            return id;
        }

        private static byte[] WriteTree(IntPtr repo, params (string Name, byte[] Id, uint Mode)[] entries)
        {
            Check(Native.NewTreeBuilder(out IntPtr builder, repo, IntPtr.Zero), "git_treebuilder_new");
            try
            {
                foreach ((string name, byte[] id, uint mode) in entries)
                {
                    Check(Native.InsertTreeEntry(IntPtr.Zero, builder, name, id, mode), "git_treebuilder_insert " + name);
                }

                byte[] treeId = new byte[OidLength];
                Check(Native.WriteTree(treeId, builder), "git_treebuilder_write");
                return treeId;
            }
            finally
            {
                Native.FreeTreeBuilder(builder);
            }
        }

        private static byte[] WriteRawObject(IntPtr repo, byte[] content, int objectType)
        {
            Check((int)LibGit2Repo.Native.Odb.GetOdb(out IntPtr odb, repo), "git_repository_odb");
            try
            {
                byte[] id = new byte[OidLength];
                Check(Native.WriteObject(id, odb, content, (UIntPtr)content.Length, objectType), "git_odb_write");
                return id;
            }
            finally
            {
                LibGit2Repo.Native.Odb.Free(odb);
            }
        }

        private static void Check(int result, string operation)
        {
            result.ShouldEqual(0, operation + " failed: " + LibGit2Repo.Native.GetLastError());
        }

        /// <summary>
        /// libgit2 functions that only these tests use, to write objects.
        /// </summary>
        private static class Native
        {
            [DllImport(LibGit2Repo.Native.Git2NativeLibName, EntryPoint = "git_repository_init")]
            public static extern int InitRepository(out IntPtr repo, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint isBare);

            [DllImport(LibGit2Repo.Native.Git2NativeLibName, EntryPoint = "git_blob_create_from_buffer")]
            public static extern int CreateBlobFromBuffer([Out] byte[] id, IntPtr repo, byte[] buffer, UIntPtr length);

            [DllImport(LibGit2Repo.Native.Git2NativeLibName, EntryPoint = "git_treebuilder_new")]
            public static extern int NewTreeBuilder(out IntPtr builder, IntPtr repo, IntPtr source);

            [DllImport(LibGit2Repo.Native.Git2NativeLibName, EntryPoint = "git_treebuilder_insert")]
            public static extern int InsertTreeEntry(IntPtr entry, IntPtr builder, [MarshalAs(UnmanagedType.LPUTF8Str)] string fileName, byte[] id, uint fileMode);

            [DllImport(LibGit2Repo.Native.Git2NativeLibName, EntryPoint = "git_treebuilder_write")]
            public static extern int WriteTree([Out] byte[] id, IntPtr builder);

            [DllImport(LibGit2Repo.Native.Git2NativeLibName, EntryPoint = "git_treebuilder_free")]
            public static extern void FreeTreeBuilder(IntPtr builder);

            [DllImport(LibGit2Repo.Native.Git2NativeLibName, EntryPoint = "git_odb_write")]
            public static extern int WriteObject([Out] byte[] id, IntPtr odb, byte[] data, UIntPtr length, int objectType);
        }
    }
}
