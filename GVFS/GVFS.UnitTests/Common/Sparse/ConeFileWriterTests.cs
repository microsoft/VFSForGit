using System;
using System.IO;
using System.Linq;
using System.Text;
using GVFS.Common.Sparse;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.FileSystem;
using NUnit.Framework;

namespace GVFS.UnitTests.Common.Sparse
{
    [TestFixture]
    public class ConeFileWriterTests
    {
        private const string RepoRoot = @"mock:\repo";
        private const string GitDir = @"mock:\repo\.git";
        private const string InfoDir = @"mock:\repo\.git\info";
        private const string SparseCheckoutPath = @"mock:\repo\.git\info\sparse-checkout";
        private const string BackupPath = SparseCheckoutPath + ConeFileWriter.BackupExtension;
        private const string Header = "/*\n!/*/\n";
        private const string LegacyContent = "/.gitattributes";
        private const string OneFileCone = Header + "/A/\n!/A/*/\n" + "/A/B/\n!/A/B/*/\n";

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void ConstructorThrowsOnNullFileSystem()
        {
            Assert.Throws<ArgumentNullException>(() => new ConeFileWriter(null));
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void SerializeThrowsOnNullCone()
        {
            Assert.Throws<ArgumentNullException>(() => ConeFileWriter.Serialize(null));
        }

        [TestCase]
        public void SerializeEmptyConeProducesHeaderOnly()
        {
            ConeFileWriter.Serialize(new ConePatternSet(new string[0], new string[0], ignoreCase: false)).ShouldEqual(Header);
        }

        [TestCase]
        public void SerializeEmitsParentPairsThenRecursiveLines()
        {
            ConePatternSet cone = new ConePatternSet(
                new[] { "A", "A/B" },
                new[] { "A/D" },
                ignoreCase: false);

            ConeFileWriter.Serialize(cone).ShouldEqual(
                Header +
                "/A/\n!/A/*/\n" +
                "/A/B/\n!/A/B/*/\n" +
                "/A/D/\n");
        }

        [TestCase("simple", "simple")]
        [TestCase("A/B", "A/B")]
        [TestCase("star*", "star\\*")]
        [TestCase("q?", "q\\?")]
        [TestCase("br[x", "br\\[x")]
        [TestCase("back\\slash", "back\\\\slash")]
        [TestCase("close]bracket", "close]bracket")]
        [TestCase("all*?[\\", "all\\*\\?\\[\\\\")]
        public void EscapePatternEscapesGlobSpecialCharacters(string input, string expected)
        {
            ConeFileWriter.EscapePattern(input).ShouldEqual(expected);
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void EscapePatternThrowsOnNull()
        {
            Assert.Throws<ArgumentNullException>(() => ConeFileWriter.EscapePattern(null));
        }

        [TestCase]
        public void WriteCreatesFileWhenNoneExists()
        {
            MockFileSystem fileSystem = CreateFileSystem();
            ConeFileWriter writer = new ConeFileWriter(fileSystem);

            writer.Write(SparseCheckoutPath, OneFileConeSet(), out string backupPath, out Exception handledException)
                .ShouldEqual(ConeFileWriteResult.Written);

            handledException.ShouldBeNull();
            backupPath.ShouldBeNull();
            fileSystem.ReadAllText(SparseCheckoutPath).ShouldEqual(OneFileCone);
            fileSystem.FileExists(SparseCheckoutPath + ".temp").ShouldBeFalse();
        }

        [TestCase]
        public void WriteBacksUpLegacyContentAndReplacesIt()
        {
            MockFileSystem fileSystem = CreateFileSystem();
            fileSystem.WriteAllText(SparseCheckoutPath, LegacyContent + "\r\n");
            ConeFileWriter writer = new ConeFileWriter(fileSystem);

            writer.Write(SparseCheckoutPath, OneFileConeSet(), out string backupPath, out Exception handledException)
                .ShouldEqual(ConeFileWriteResult.Written);

            handledException.ShouldBeNull();
            backupPath.ShouldEqual(BackupPath);
            fileSystem.ReadAllText(backupPath).ShouldEqual(LegacyContent + "\r\n");
            fileSystem.ReadAllText(SparseCheckoutPath).ShouldEqual(OneFileCone);
        }

        [TestCase]
        public void WriteWithUnchangedContentWritesNothing()
        {
            MockFileSystem fileSystem = CreateFileSystem();
            fileSystem.WriteAllText(SparseCheckoutPath, OneFileCone);
            ConeFileWriter writer = new ConeFileWriter(fileSystem);

            writer.Write(SparseCheckoutPath, OneFileConeSet(), out string backupPath, out Exception handledException)
                .ShouldEqual(ConeFileWriteResult.Unchanged);

            handledException.ShouldBeNull();
            backupPath.ShouldBeNull();
            fileSystem.FileExists(BackupPath).ShouldBeFalse();
            fileSystem.ReadAllText(SparseCheckoutPath).ShouldEqual(OneFileCone);
        }

        [TestCase]
        public void WriteTreatsDifferentLineEndingsAsChangedContent()
        {
            // Git reads either line ending, but the writer compares exact content. A file with
            // CRLF line endings is rewritten with LF, as git's own writer would write it.
            MockFileSystem fileSystem = CreateFileSystem();
            fileSystem.WriteAllText(SparseCheckoutPath, OneFileCone.Replace("\n", "\r\n"));
            ConeFileWriter writer = new ConeFileWriter(fileSystem);

            writer.Write(SparseCheckoutPath, OneFileConeSet(), out _, out _).ShouldEqual(ConeFileWriteResult.Written);

            fileSystem.ReadAllText(SparseCheckoutPath).ShouldEqual(OneFileCone);
        }

        [TestCase]
        public void WriteReplacesSameTextInAnotherEncoding()
        {
            // Git reads bytes. It cannot parse a UTF-16 file, even when the decoded text is the
            // same as the serialized cone.
            MockFileSystem fileSystem = CreateFileSystem();
            byte[] utf16Content = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(OneFileCone)).ToArray();
            WriteBytes(fileSystem, SparseCheckoutPath, utf16Content);
            ConeFileWriter writer = new ConeFileWriter(fileSystem);

            writer.Write(SparseCheckoutPath, OneFileConeSet(), out string backupPath, out _).ShouldEqual(ConeFileWriteResult.Written);

            fileSystem.ReadAllBytes(SparseCheckoutPath).ShouldMatchInOrder(Encoding.UTF8.GetBytes(OneFileCone));
            fileSystem.ReadAllBytes(backupPath).ShouldMatchInOrder(utf16Content);
        }

        [TestCase]
        public void BackupAndRestoreKeepTheExactBytes()
        {
            // 0xE9 is not valid UTF-8. A text round trip would change it to U+FFFD.
            byte[] originalBytes = new byte[] { (byte)'/', (byte)'*', (byte)'\n', 0xE9, (byte)'\n' };
            MockFileSystem fileSystem = CreateFileSystem();
            WriteBytes(fileSystem, SparseCheckoutPath, originalBytes);
            ConeFileWriter writer = new ConeFileWriter(fileSystem);

            writer.Write(SparseCheckoutPath, OneFileConeSet(), out string backupPath, out _).ShouldEqual(ConeFileWriteResult.Written);
            fileSystem.ReadAllBytes(backupPath).ShouldMatchInOrder(originalBytes);

            writer.TryRestoreBackup(SparseCheckoutPath, backupPath, out Exception restoreException).ShouldBeTrue();
            restoreException.ShouldBeNull();
            fileSystem.ReadAllBytes(SparseCheckoutPath).ShouldMatchInOrder(originalBytes);
        }

        [TestCase]
        public void WriteWithUnchangedNonAsciiContentWritesNothing()
        {
            ConePatternSet cone = ConeBuilder.BuildFromModifiedPaths(new[] { "\u00C4/x.txt" }, ignoreCase: false);
            MockFileSystem fileSystem = CreateFileSystem();
            WriteBytes(fileSystem, SparseCheckoutPath, Encoding.UTF8.GetBytes(Header + "/\u00C4/\n!/\u00C4/*/\n"));
            ConeFileWriter writer = new ConeFileWriter(fileSystem);

            writer.Write(SparseCheckoutPath, cone, out string backupPath, out _).ShouldEqual(ConeFileWriteResult.Unchanged);

            backupPath.ShouldBeNull();
        }

        [TestCase(false)]
        [TestCase(true)]
        [Category(CategoryConstants.ExceptionExpected)]
        public void WriteFailsWhenExistingFileCannotBeRead(bool accessDenied)
        {
            // A file that cannot be read must not look like a missing file, or the write would
            // replace it without a backup.
            Exception readFailure = accessDenied ? new UnauthorizedAccessException("Injected access failure") : new IOException("Injected read failure");
            FailingFileSystem fileSystem = new FailingFileSystem { FailReadPath = SparseCheckoutPath, ReadFailure = readFailure };
            fileSystem.WriteAllText(SparseCheckoutPath, LegacyContent);
            ConeFileWriter writer = new ConeFileWriter(fileSystem);

            writer.Write(SparseCheckoutPath, OneFileConeSet(), out string backupPath, out Exception handledException)
                .ShouldEqual(ConeFileWriteResult.Failed);

            backupPath.ShouldBeNull();
            handledException.ShouldBeSameAs(readFailure);
            fileSystem.FileExists(BackupPath).ShouldBeFalse();
            fileSystem.FailReadPath = null;
            fileSystem.ReadAllText(SparseCheckoutPath).ShouldEqual(LegacyContent);
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void WriteFailsAndKeepsTargetWhenBackupCannotBeWritten()
        {
            FailingFileSystem fileSystem = new FailingFileSystem { FailRenameDestination = BackupPath };
            fileSystem.WriteAllText(SparseCheckoutPath, LegacyContent);
            ConeFileWriter writer = new ConeFileWriter(fileSystem);

            writer.Write(SparseCheckoutPath, OneFileConeSet(), out string backupPath, out Exception handledException)
                .ShouldEqual(ConeFileWriteResult.Failed);

            backupPath.ShouldBeNull();
            handledException.ShouldBeOfType<IOException>();
            fileSystem.ReadAllText(SparseCheckoutPath).ShouldEqual(LegacyContent);
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void WriteFailsAndKeepsTargetWhenRenameFails()
        {
            // The new content goes to a temporary file first. If the rename over the target
            // fails, the target keeps its previous content, and the backup path is still
            // returned so that the caller can roll back other state.
            FailingFileSystem fileSystem = new FailingFileSystem { FailRenameDestination = SparseCheckoutPath };
            fileSystem.WriteAllText(SparseCheckoutPath, LegacyContent);
            ConeFileWriter writer = new ConeFileWriter(fileSystem);

            writer.Write(SparseCheckoutPath, OneFileConeSet(), out string backupPath, out Exception handledException)
                .ShouldEqual(ConeFileWriteResult.Failed);

            backupPath.ShouldEqual(BackupPath);
            handledException.ShouldBeOfType<IOException>();
            fileSystem.ReadAllText(SparseCheckoutPath).ShouldEqual(LegacyContent);
            fileSystem.ReadAllText(BackupPath).ShouldEqual(LegacyContent);
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void WriteFailsWhenInfoDirectoryCannotBeCreated()
        {
            MockFileSystem fileSystem = CreateFileSystem();
            fileSystem.ExceptionThrownByCreateDirectory = new UnauthorizedAccessException("denied");
            ConeFileWriter writer = new ConeFileWriter(fileSystem);

            writer.Write(SparseCheckoutPath, OneFileConeSet(), out _, out Exception handledException)
                .ShouldEqual(ConeFileWriteResult.Failed);

            handledException.ShouldBeOfType<UnauthorizedAccessException>();
        }

        [TestCase]
        public void TryRestoreBackupRestoresPreviousContent()
        {
            MockFileSystem fileSystem = CreateFileSystem();
            fileSystem.WriteAllText(SparseCheckoutPath, LegacyContent + "\r\n");
            ConeFileWriter writer = new ConeFileWriter(fileSystem);

            writer.Write(SparseCheckoutPath, OneFileConeSet(), out string backupPath, out _).ShouldEqual(ConeFileWriteResult.Written);

            writer.TryRestoreBackup(SparseCheckoutPath, backupPath, out Exception restoreException).ShouldBeTrue();

            restoreException.ShouldBeNull();
            fileSystem.ReadAllText(SparseCheckoutPath).ShouldEqual(LegacyContent + "\r\n");
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void TryRestoreBackupFailsWhenBackupCannotBeRead()
        {
            FailingFileSystem fileSystem = new FailingFileSystem { FailReadPath = BackupPath };
            fileSystem.WriteAllText(SparseCheckoutPath, OneFileCone);
            fileSystem.WriteAllText(BackupPath, LegacyContent);
            ConeFileWriter writer = new ConeFileWriter(fileSystem);

            writer.TryRestoreBackup(SparseCheckoutPath, BackupPath, out Exception restoreException).ShouldBeFalse();

            restoreException.ShouldBeOfType<IOException>();
            fileSystem.ReadAllText(SparseCheckoutPath).ShouldEqual(OneFileCone);
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void TryRestoreBackupFailsWhenBackupIsMissing()
        {
            MockFileSystem fileSystem = CreateFileSystem();
            fileSystem.WriteAllText(SparseCheckoutPath, OneFileCone);
            ConeFileWriter writer = new ConeFileWriter(fileSystem);

            writer.TryRestoreBackup(SparseCheckoutPath, BackupPath, out Exception restoreException).ShouldBeFalse();

            restoreException.ShouldBeOfType<FileNotFoundException>();
            fileSystem.ReadAllText(SparseCheckoutPath).ShouldEqual(OneFileCone);
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void WriteThrowsOnNullArguments()
        {
            ConeFileWriter writer = new ConeFileWriter(CreateFileSystem());
            ConePatternSet cone = new ConePatternSet(new string[0], new string[0], ignoreCase: false);

            Assert.Throws<ArgumentNullException>(() => writer.Write(null, cone, out _, out _));
            Assert.Throws<ArgumentNullException>(() => writer.Write(SparseCheckoutPath, null, out _, out _));
        }

        private static ConePatternSet OneFileConeSet()
        {
            return ConeBuilder.BuildFromModifiedPaths(new[] { "A/B/b.txt" }, ignoreCase: false);
        }

        private static void WriteBytes(MockFileSystem fileSystem, string path, byte[] contents)
        {
            fileSystem.RootDirectory.AddOrOverwriteFile(new MockFile(path, contents), path);
        }

        private static MockFileSystem CreateFileSystem()
        {
            MockDirectory info = new MockDirectory(InfoDir, folders: null, files: null);
            MockDirectory git = new MockDirectory(GitDir, new[] { info }, files: null);
            MockDirectory root = new MockDirectory(RepoRoot, new[] { git }, files: null);
            return new MockFileSystem(root);
        }

        private sealed class FailingFileSystem : MockFileSystem
        {
            public FailingFileSystem()
                : base(CreateFileSystem().RootDirectory)
            {
            }

            public string FailReadPath { get; set; }

            public Exception ReadFailure { get; set; }

            public string FailRenameDestination { get; set; }

            public override string ReadAllText(string path)
            {
                this.ThrowIfReadFails(path);
                return base.ReadAllText(path);
            }

            public override byte[] ReadAllBytes(string path)
            {
                this.ThrowIfReadFails(path);
                return base.ReadAllBytes(path);
            }

            public override void MoveAndOverwriteFile(string sourcePath, string destinationPath)
            {
                if (string.Equals(destinationPath, this.FailRenameDestination, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("Injected rename failure");
                }

                base.MoveAndOverwriteFile(sourcePath, destinationPath);
            }

            private void ThrowIfReadFails(string path)
            {
                if (string.Equals(path, this.FailReadPath, StringComparison.OrdinalIgnoreCase))
                {
                    throw this.ReadFailure ?? new IOException("Injected read failure");
                }
            }
        }
    }
}
