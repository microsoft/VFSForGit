using GVFS.Common.Database;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Virtual;
using GVFS.Virtualization.Projection;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using static GVFS.Virtualization.Projection.GitIndexProjection;
using static GVFS.Virtualization.Projection.GitIndexProjection.GitIndexParser;

namespace GVFS.UnitTests.Virtualization.Git
{
    [TestFixture]
    public class GitIndexProjectionRebuildTests : TestsWithCommonRepo
    {
        [TestCase(false, false)]
        [TestCase(true, true)]
        public void SkippedEntryInvalidatesReusableNames(bool skipWorktree, bool commonAncestor)
        {
            using (GitIndexProjection projection = this.CreateProjection())
            {
                GitIndexParser parser = new GitIndexParser(projection);
                Rebuild(parser,
                    ("a/b/first.txt", true, MergeStage.NoConflicts),
                    ("a/c/ignored.txt", skipWorktree, commonAncestor ? MergeStage.CommonAncestor : MergeStage.NoConflicts),
                    ("a/c/last.txt", true, MergeStage.NoConflicts));

                Assert.AreEqual(6, LazyUTF8String.StringPoolObjectsUsed());
                Assert.IsTrue(projection.IsPathProjected(Path.Combine("a", "b", "first.txt"), out string _, out bool _));
                Assert.IsFalse(projection.IsPathProjected(Path.Combine("a", "c", "ignored.txt"), out string _, out bool _));
                Assert.IsTrue(projection.IsPathProjected(Path.Combine("a", "c", "last.txt"), out string _, out bool _));
            }
        }

        [TestCase]
        public void MergeStageChangesPreserveReusableNames()
        {
            using (GitIndexProjection projection = this.CreateProjection())
            {
                GitIndexParser parser = new GitIndexParser(projection);
                Rebuild(parser,
                    ("a/b/first.txt", true, MergeStage.NoConflicts),
                    ("a/b/ours.txt", false, MergeStage.Yours),
                    ("a/b/theirs.txt", true, MergeStage.Theirs),
                    ("a/b/last.txt", true, MergeStage.NoConflicts));

                Assert.AreEqual(6, LazyUTF8String.StringPoolObjectsUsed());
                CollectionAssert.AreEqual(
                    new[] { "first.txt", "last.txt", "ours.txt", "theirs.txt" },
                    projection.GetProjectedItems(CancellationToken.None, null, Path.Combine("a", "b")).Select(item => item.Name));
            }
        }

        [TestCase]
        public void RebuildPreservesProjectionAcrossPoolReuse()
        {
            using (GitIndexProjection projection = this.CreateProjection())
            {
                GitIndexParser parser = new GitIndexParser(projection);
                for (int i = 0; i < 3; ++i)
                {
                    Rebuild(parser,
                        ("a/b/first.txt", true, MergeStage.NoConflicts),
                        ("a/c/last.txt", true, MergeStage.NoConflicts));
                    Assert.AreEqual(5, LazyUTF8String.StringPoolObjectsUsed());
                    Assert.AreEqual(3, projection.GetProjectedFolderCount());
                    Assert.IsTrue(projection.IsPathProjected(Path.Combine("a", "c", "last.txt"), out string _, out bool _));

                    Rebuild(parser, ("root.txt", true, MergeStage.NoConflicts));
                    Assert.AreEqual(1, LazyUTF8String.StringPoolObjectsUsed());
                    Assert.AreEqual(0, projection.GetProjectedFolderCount());
                    Assert.IsFalse(projection.IsPathProjected(Path.Combine("a", "c", "last.txt"), out string _, out bool _));
                    CollectionAssert.AreEqual(
                        new[] { "root.txt" },
                        projection.GetProjectedItems(CancellationToken.None, null, string.Empty).Select(item => item.Name));

                    Rebuild(parser);
                    Assert.AreEqual(0, LazyUTF8String.StringPoolObjectsUsed());
                    Assert.IsEmpty(projection.GetProjectedItems(CancellationToken.None, null, string.Empty));
                }
            }
        }

        [TestCase]
        public void RealIndexEntryShapesPreserveProjection()
        {
            string longDirectory = new string('x', 150);
            using (GitIndexProjection projection = this.CreateProjection())
            {
                GitIndexParser parser = new GitIndexParser(projection);
                Rebuild(parser,
                    ("a/b/conflict.txt", false, MergeStage.CommonAncestor),
                    ("a/b/conflict.txt", false, MergeStage.Yours),
                    ("a/b/conflict.txt", false, MergeStage.Theirs),
                    ("a/b/next.txt", true, MergeStage.NoConflicts),
                    (longDirectory + "/file.txt", true, MergeStage.NoConflicts),
                    ("\u00e9/\u76ee\u5f55/file.txt", true, MergeStage.NoConflicts),
                    ("\u00e9/\u76ee\u5f13/file.txt", true, MergeStage.NoConflicts));

                Assert.AreEqual(13, LazyUTF8String.StringPoolObjectsUsed());
                Assert.AreEqual(6, projection.GetProjectedFolderCount());
                CollectionAssert.AreEqual(
                    new[] { "conflict.txt", "next.txt" },
                    projection.GetProjectedItems(CancellationToken.None, null, Path.Combine("a", "b")).Select(item => item.Name));
                foreach (string directory in new[] { longDirectory, Path.Combine("\u00e9", "\u76ee\u5f55"), Path.Combine("\u00e9", "\u76ee\u5f13") })
                {
                    Assert.IsTrue(projection.IsPathProjected(Path.Combine(directory, "file.txt"), out string _, out bool _));
                }
            }
        }

        [TestCase]
        [Category(CategoryConstants.CaseInsensitiveFileSystemOnly)]
        public void PrefixReusePreservesUnicodeAndFirstFolderCasing()
        {
            using (GitIndexProjection projection = this.CreateProjection())
            {
                GitIndexParser parser = new GitIndexParser(projection);
                Rebuild(parser,
                    ("A/\u00e9/first/file.txt", true, MergeStage.NoConflicts),
                    ("A/\u00e9/second/file.txt", true, MergeStage.NoConflicts),
                    ("a/\u00e9/third/file.txt", true, MergeStage.NoConflicts));

                Assert.AreEqual(10, LazyUTF8String.StringPoolObjectsUsed());
                Assert.AreEqual(5, projection.GetProjectedFolderCount());
                CollectionAssert.AreEqual(
                    new[] { "A" },
                    projection.GetProjectedItems(CancellationToken.None, null, string.Empty).Select(item => item.Name));
                CollectionAssert.AreEqual(
                    new[] { "first", "second", "third" },
                    projection.GetProjectedItems(CancellationToken.None, null, Path.Combine("a", "\u00e9")).Select(item => item.Name));
                Assert.IsTrue(projection.IsPathProjected(Path.Combine("a", "\u00c9", "THIRD", "FILE.TXT"), out string _, out bool _));
            }
        }

        [TestCase]
        public void PrefixReusePreservesSparseFolders()
        {
            Mock<ISparseCollection> sparse = new Mock<ISparseCollection>(MockBehavior.Strict);
            sparse.Setup(collection => collection.GetAll()).Returns(new HashSet<string> { Path.Combine("a", "b") });
            using (GitIndexProjection projection = this.CreateProjection(sparse.Object))
            {
                GitIndexParser parser = new GitIndexParser(projection);
                Rebuild(parser,
                    ("a/b/left/file.txt", true, MergeStage.NoConflicts),
                    ("a/b/right/file.txt", true, MergeStage.NoConflicts),
                    ("a/c/left/file.txt", true, MergeStage.NoConflicts),
                    ("a/c/right/file.txt", true, MergeStage.NoConflicts));
                Assert.AreEqual(11, LazyUTF8String.StringPoolObjectsUsed());
                Assert.AreEqual(PathSparseState.Included, projection.GetFolderPathSparseState(Path.Combine("a", "b", "right")));
                Assert.AreEqual(PathSparseState.Excluded, projection.GetFolderPathSparseState(Path.Combine("a", "c", "right")));
                Assert.IsTrue(projection.IsPathProjected(Path.Combine("a", "b", "right", "file.txt"), out string _, out bool _));
                Assert.IsFalse(projection.IsPathProjected(Path.Combine("a", "c", "right", "file.txt"), out string _, out bool _));
            }

            sparse.VerifyAll();
        }

        private static void Rebuild(GitIndexParser parser, params (string Path, bool SkipWorktree, MergeStage Stage)[] entries)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(Encoding.ASCII.GetBytes("DIRC"));
                writer.Write(EndianHelper.Swap((uint)4));
                writer.Write(EndianHelper.Swap((uint)entries.Length));
                byte[] previousPath = Array.Empty<byte>();
                foreach ((string path, bool skipWorktree, MergeStage stage) in entries)
                {
                    byte[] stat = new byte[40];
                    stat[26] = 0x81;
                    stat[27] = 0xA4;
                    writer.Write(stat);
                    writer.Write(new byte[20]);
                    byte[] pathBytes = Encoding.UTF8.GetBytes(path);
                    ushort flags = (ushort)(pathBytes.Length | ((int)stage << 12));
                    if (skipWorktree)
                    {
                        flags |= 0x4000;
                    }

                    writer.Write(EndianHelper.Swap(flags));
                    if (skipWorktree)
                    {
                        writer.Write(EndianHelper.Swap((ushort)0x4000));
                    }

                    int prefixLength = 0;
                    while (prefixLength < Math.Min(previousPath.Length, pathBytes.Length) &&
                        previousPath[prefixLength] == pathBytes[prefixLength])
                    {
                        ++prefixLength;
                    }

                    WriteReplaceLength(writer, previousPath.Length - prefixLength);
                    writer.Write(pathBytes, prefixLength, pathBytes.Length - prefixLength);
                    writer.Write((byte)0);
                    previousPath = pathBytes;
                }

                writer.Flush();
                parser.RebuildProjection(NullTracer.Instance, stream);
            }
        }

        private static void WriteReplaceLength(BinaryWriter writer, int value)
        {
            byte[] encoded = new byte[5];
            int offset = encoded.Length - 1;
            encoded[offset] = (byte)(value & 0x7F);
            while ((value >>= 7) > 0)
            {
                --value;
                encoded[--offset] = (byte)(0x80 | (value & 0x7F));
            }

            writer.Write(encoded, offset, encoded.Length - offset);
        }

        private GitIndexProjection CreateProjection(ISparseCollection sparse = null)
        {
            return new GitIndexProjection(
                this.Repo.Context,
                gitObjects: null,
                blobSizes: null,
                repoMetadata: null,
                fileSystemVirtualizer: null,
                placeholderDatabase: null,
                sparseCollection: sparse,
                modifiedPaths: null);
        }
    }
}
