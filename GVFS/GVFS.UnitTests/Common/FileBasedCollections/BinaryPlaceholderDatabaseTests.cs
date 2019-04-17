using GVFS.Common;
using GVFS.Common.FileBasedCollections;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock;
using GVFS.UnitTests.Mock.FileSystem;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

// GVFS.UnitTests.Common.FileBasedCollections.BinaryPlaceholderDatabaseTests.ParsesExistingDataCorrectly
namespace GVFS.UnitTests.Common.FileBasedCollections
{
    [TestFixture]
    public class BinaryPlaceholderDatabaseTests
    {
        private const string MockEntryFileName = "mock:\\entries.dat";

        private const string InputGitIgnorePath = ".gitignore";
        private const string InputGitIgnoreSHA = "AE930E4CF715315FC90D4AEC98E16A7398F8BF64";

        private const string InputGitAttributesPath = ".gitattributes";
        private const string InputGitAttributesSHA = "BB9630E4CF715315FC90D4AEC98E167398F8BF66";

        private const string InputThirdFilePath = "thirdFile";
        private const string InputThirdFileSHA = "ff9630E00F715315FC90D4AEC98E6A7398F8BF11";

        private static readonly byte[] TerminatingBytes = new byte[] { 0, 0, 0, 0 };

        // "A " + InputGitIgnorePath + "\0" + InputGitIgnoreSHA + PlaceholderDatabaseNewLine;
        private readonly byte[] expectedGitIgnoreEntry = new byte[] { 1, 1 }.Concat(GetStringBytes(InputGitIgnorePath)).Concat(InputGitIgnoreSHA.Select(c => (byte)c)).Concat(TerminatingBytes).ToArray();

        // "A " + InputGitAttributesPath + "\0" + InputGitAttributesSHA + PlaceholderDatabaseNewLine;
        private readonly byte[] expectedGitAttributesEntry = new byte[] { 1, 1 }.Concat(GetStringBytes(InputGitAttributesPath)).Concat(InputGitAttributesSHA.Select(c => (byte)c)).Concat(TerminatingBytes).ToArray();

        private byte[] ExpectedTwoEntries => this.expectedGitIgnoreEntry.Concat(this.expectedGitAttributesEntry).ToArray();

        [TestCase]
        public void ParsesExistingDataCorrectly()
        {
            ConfigurableFileSystem fs = new ConfigurableFileSystem();
            BinaryPlaceholderListDatabase dut = CreatePlaceholderListDatabase(
                fs,
                Enumerable.Empty<byte>()

                // "A .gitignore\0AE930E4CF715315FC90D4AEC98E16A7398F8BF64\r\n" +
                .Concat(new byte[] { 1, 1 }).Concat(GetStringBytes(".gitignore")).Concat("AE930E4CF715315FC90D4AEC98E16A7398F8BF64".Select(c => (byte)c)).Concat(TerminatingBytes).ToArray()

                // "A Test_EPF_UpdatePlaceholderTests\\LockToPreventDelete\\test.txt\0B6948308A8633CC1ED94285A1F6BF33E35B7C321\r\n" +
                .Concat(new byte[] { 1, 1 }).Concat(GetStringBytes("Test_EPF_UpdatePlaceholderTests\\LockToPreventDelete\\test.txt")).Concat("B6948308A8633CC1ED94285A1F6BF33E35B7C321".Select(c => (byte)c)).Concat(TerminatingBytes).ToArray()

                // "A Test_EPF_UpdatePlaceholderTests\\LockToPreventDelete\\test.txt\0C7048308A8633CC1ED94285A1F6BF33E35B7C321\r\n" +
                .Concat(new byte[] { 1, 1 }).Concat(GetStringBytes("Test_EPF_UpdatePlaceholderTests\\LockToPreventDelete\\test.txt")).Concat("C7048308A8633CC1ED94285A1F6BF33E35B7C321".Select(c => (byte)c)).Concat(TerminatingBytes).ToArray()

                // "A Test_EPF_UpdatePlaceholderTests\\LockToPreventDelete\\test2.txt\0D19198D6EA60F0D66F0432FEC6638D0A73B16E81\r\n" +
                .Concat(new byte[] { 1, 1 }).Concat(GetStringBytes("Test_EPF_UpdatePlaceholderTests\\LockToPreventDelete\\test2.txt")).Concat("D19198D6EA60F0D66F0432FEC6638D0A73B16E81".Select(c => (byte)c)).Concat(TerminatingBytes).ToArray()

                // "A Test_EPF_UpdatePlaceholderTests\\LockToPreventDelete\\test3.txt\0E45EA0D328E581696CAF1F823686F3665A5F05C1\r\n" +
                .Concat(new byte[] { 1, 1 }).Concat(GetStringBytes("Test_EPF_UpdatePlaceholderTests\\LockToPreventDelete\\test3.txt")).Concat("E45EA0D328E581696CAF1F823686F3665A5F05C1".Select(c => (byte)c)).Concat(TerminatingBytes).ToArray()

                // "A Test_EPF_UpdatePlaceholderTests\\LockToPreventDelete\\test4.txt\0FCB3E2C561649F102DD8110A87DA82F27CC05833\r\n" +
                .Concat(new byte[] { 1, 1 }).Concat(GetStringBytes("Test_EPF_UpdatePlaceholderTests\\LockToPreventDelete\\test4.txt")).Concat("FCB3E2C561649F102DD8110A87DA82F27CC05833".Select(c => (byte)c)).Concat(TerminatingBytes).ToArray()

                // "A Test_EPF_UpdatePlaceholderTests\\LockToPreventUpdate\\test.txt\0E51B377C95076E4C6A9E22A658C5690F324FD0AD\r\n" +
                .Concat(new byte[] { 1, 1 }).Concat(GetStringBytes("Test_EPF_UpdatePlaceholderTests\\LockToPreventUpdate\\test.txt")).Concat("E51B377C95076E4C6A9E22A658C5690F324FD0AD".Select(c => (byte)c)).Concat(TerminatingBytes).ToArray()

                // "D Test_EPF_UpdatePlaceholderTests\\LockToPreventUpdate\\test.txt\r\n" +
                .Concat(new byte[] { 2 }).Concat(GetStringBytes("Test_EPF_UpdatePlaceholderTests\\LockToPreventUpdate\\test.txt")).Concat(TerminatingBytes).ToArray()

                // "D Test_EPF_UpdatePlaceholderTests\\LockToPreventUpdate\\test.txt\r\n" +
                .Concat(new byte[] { 2 }).Concat(GetStringBytes("Test_EPF_UpdatePlaceholderTests\\LockToPreventUpdate\\test.txt")).Concat(TerminatingBytes).ToArray()

                // "D Test_EPF_UpdatePlaceholderTests\\LockToPreventUpdate\\test.txt\r\n");
                .Concat(new byte[] { 2 }).Concat(GetStringBytes("Test_EPF_UpdatePlaceholderTests\\LockToPreventUpdate\\test.txt")).Concat(TerminatingBytes).ToArray());

            // As per documentation on EstimatedCount "(# adds - # deletes)" - so EstimatedCount should be 5
            dut.EstimatedCount.ShouldEqual(4);

            // The actual count is here
            dut.GetAllEntriesAndPrepToWriteAllEntries().Count.ShouldEqual(5);
        }

        [TestCase]
        public void WritesPlaceholderAddToFile()
        {
            ConfigurableFileSystem fs = new ConfigurableFileSystem();
            BinaryPlaceholderListDatabase dut = CreatePlaceholderListDatabase(fs, new byte[0]);
            dut.AddAndFlushFile(InputGitIgnorePath, InputGitIgnoreSHA);

            fs.ExpectedFiles[MockEntryFileName].ReadAllBytes().ShouldEqual(this.expectedGitIgnoreEntry);

            dut.AddAndFlushFile(InputGitAttributesPath, InputGitAttributesSHA);

            fs.ExpectedFiles[MockEntryFileName].ReadAllBytes().ShouldEqual(this.ExpectedTwoEntries);
        }

        [TestCase]
        public void GetAllEntriesAndPrepToWriteAllEntriesReturnsCorrectEntries()
        {
            ConfigurableFileSystem fs = new ConfigurableFileSystem();
            using (BinaryPlaceholderListDatabase dut1 = CreatePlaceholderListDatabase(fs, new byte[0]))
            {
                dut1.AddAndFlushFile(InputGitIgnorePath, InputGitIgnoreSHA);
                dut1.AddAndFlushFile(InputGitAttributesPath, InputGitAttributesSHA);
                dut1.AddAndFlushFile(InputThirdFilePath, InputThirdFileSHA);
                dut1.RemoveAndFlush(InputThirdFilePath);
            }

            string error;
            BinaryPlaceholderListDatabase dut2;
            BinaryPlaceholderListDatabase.TryCreate(null, MockEntryFileName, fs, out dut2, out error).ShouldEqual(true, error);
            List<PlaceholderEvent> allData = dut2.GetAllEntriesAndPrepToWriteAllEntries();
            allData.Count.ShouldEqual(2);
        }

        [TestCase]
        public void GetAllEntriesAndPrepToWriteAllEntriesSplitsFilesAndFoldersCorrectly()
        {
            ConfigurableFileSystem fs = new ConfigurableFileSystem();
            using (BinaryPlaceholderListDatabase dut1 = CreatePlaceholderListDatabase(fs, new byte[0]))
            {
                dut1.AddAndFlushFile(InputGitIgnorePath, InputGitIgnoreSHA);
                dut1.AddAndFlushFolder("partialFolder", isExpanded: false);
                dut1.AddAndFlushFile(InputGitAttributesPath, InputGitAttributesSHA);
                dut1.AddAndFlushFolder("expandedFolder", isExpanded: true);
                dut1.AddAndFlushFile(InputThirdFilePath, InputThirdFileSHA);
                dut1.RemoveAndFlush(InputThirdFilePath);
            }

            string error;
            BinaryPlaceholderListDatabase dut2;
            BinaryPlaceholderListDatabase.TryCreate(null, MockEntryFileName, fs, out dut2, out error).ShouldEqual(true, error);
            IReadOnlyList<AddFileEntry> fileData;
            IReadOnlyList<AddFolderEntry> folderData;
            dut2.GetAllEntriesAndPrepToWriteAllEntries(out fileData, out folderData);
            fileData.Count.ShouldEqual(2);
            folderData.Count.ShouldEqual(2);
            folderData.ShouldContain(
                new[]
                {
                    new AddFolderEntry("partialFolder", false),
                    new AddFolderEntry("expandedFolder", true)
                },
                (data1, data2) => data1.Path == data2.Path && data1.IsExpandedFolder == data2.IsExpandedFolder);
        }

        [TestCase]
        public void WriteAllEntriesCorrectlyWritesFile()
        {
            ConfigurableFileSystem fs = new ConfigurableFileSystem();
            fs.ExpectedFiles.Add(MockEntryFileName + ".tmp", new ReusableMemoryStream(new byte[0]));

            BinaryPlaceholderListDatabase dut = CreatePlaceholderListDatabase(fs, new byte[0]);

            List<AddFileEntry> allData = new List<AddFileEntry>()
            {
                new AddFileEntry(InputGitIgnorePath, InputGitIgnoreSHA),
                new AddFileEntry(InputGitAttributesPath, InputGitAttributesSHA)
            };

            dut.WriteAllEntriesAndFlush(allData);
            fs.ExpectedFiles[MockEntryFileName].ReadAllBytes().ShouldEqual(this.ExpectedTwoEntries);
        }

        [TestCase]
        public void HandlesRaceBetweenAddAndWriteAllEntries()
        {
            ConfigurableFileSystem fs = new ConfigurableFileSystem();
            fs.ExpectedFiles.Add(MockEntryFileName + ".tmp", new ReusableMemoryStream(string.Empty));

            BinaryPlaceholderListDatabase dut = CreatePlaceholderListDatabase(fs, this.expectedGitIgnoreEntry);

            List<PlaceholderEvent> existingEntries = dut.GetAllEntriesAndPrepToWriteAllEntries();

            dut.AddAndFlushFile(InputGitAttributesPath, InputGitAttributesSHA);

            dut.WriteAllEntriesAndFlush(existingEntries);
            fs.ExpectedFiles[MockEntryFileName].ReadAllBytes().ShouldEqual(this.ExpectedTwoEntries);
        }

        [TestCase]
        public void HandlesRaceBetweenRemoveAndWriteAllEntries()
        {
            // "D .gitattributes" + PlaceholderDatabaseNewLine
            byte[] deleteGitAttributesEntry = new byte[] { 2 }.Concat(GetStringBytes(".gitattributes")).Concat(TerminatingBytes).ToArray();

            ConfigurableFileSystem fs = new ConfigurableFileSystem();
            fs.ExpectedFiles.Add(MockEntryFileName + ".tmp", new ReusableMemoryStream(string.Empty));

            BinaryPlaceholderListDatabase dut = CreatePlaceholderListDatabase(fs, this.ExpectedTwoEntries);

            List<PlaceholderEvent> existingEntries = dut.GetAllEntriesAndPrepToWriteAllEntries();

            dut.RemoveAndFlush(InputGitAttributesPath);

            dut.WriteAllEntriesAndFlush(existingEntries);
            fs.ExpectedFiles[MockEntryFileName].ReadAllBytes().ShouldEqual(this.ExpectedTwoEntries.Concat(deleteGitAttributesEntry).ToArray());
        }

        [TestCase]
        public void HandlesCorruptEntriesCorrectly()
        {
            ConfigurableFileSystem fs = new ConfigurableFileSystem();
            BinaryPlaceholderListDatabase dut = CreatePlaceholderListDatabase(
                fs,
                Enumerable.Empty<byte>()

                // "A .gitignore\0AE930E4CF715315FC90D4AEC98E16A7398F8BF64\r\n" +
                .Concat(new byte[] { 1, 1 }).Concat(GetStringBytes(".gitignore")).Concat("AE930E4CF715315FC90D4AEC98E16A7398F8BF64".Select(c => (byte)c)).Concat(TerminatingBytes).ToArray()

                // "A Test_EPF_UpdatePlaceholderTests\\LockToPreventDelete\\test.txt\0B6948308A8633CC1ED94285A1F6BF33E35B7C321\r\n" +
                .Concat(new byte[] { 1, 1 }).Concat(GetStringBytes("Test_EPF_UpdatePlaceholderTests\\LockToPreventDelete\\test.txt")).Concat("B6948308A8633CC1ED94285A1F6BF33E35B7C321".Select(c => (byte)c)).ToArray()

                // "A Test_EPF_UpdatePlaceholderTests\\LockToPreventDelete\\test.txt\0C7048308A8633CC1ED94285A1F6BF33E35B7C321\r\n" +
                .Concat(new byte[] { 1, 1 }).Concat(GetStringBytes("Test_EPF_UpdatePlaceholderTests\\LockToPreventDelete\\test.txt")).Concat("C7048308A8633CC1ED94285A1F6BF33E35B7C321".Select(c => (byte)c)).Concat(new byte[] { 0, 0, 0 }).ToArray());

            // As per documentation on EstimatedCount "(# adds - # deletes)" - so EstimatedCount should be 5
            dut.EstimatedCount.ShouldEqual(1);

            // The actual count is here
            dut.GetAllEntriesAndPrepToWriteAllEntries().Count.ShouldEqual(1);
        }

        [TestCase]
        public void TestPerformance()
        {
            const int numberOfPlaceholders = 100000;
            ConfigurableFileSystem fs = new ConfigurableFileSystem();
            var sw = new System.Diagnostics.Stopwatch();

            using (BinaryPlaceholderListDatabase dut1 = CreatePlaceholderListDatabase(fs, new byte[0]))
            {
                for (int i = 0; i < numberOfPlaceholders; i++)
                {
                    dut1.AddAndFlushFile(System.Guid.NewGuid().ToString(), InputGitIgnoreSHA);
                }
            }

            string error;
            BinaryPlaceholderListDatabase dut2;
            BinaryPlaceholderListDatabase.TryCreate(null, MockEntryFileName, fs, out dut2, out error).ShouldEqual(true, error);
            sw.Restart();
            List<PlaceholderEvent> allData = dut2.GetAllEntriesAndPrepToWriteAllEntries();
            sw.Stop();
            System.Console.WriteLine($"Loading {numberOfPlaceholders} took {sw.ElapsedMilliseconds}ms");
            sw.ElapsedMilliseconds.ShouldBeAtMost(300);
            allData.Count.ShouldEqual(numberOfPlaceholders);
        }

        private static byte[] GetStringBytes(string s)
        {
            if (Encoding.UTF8.GetByteCount(s) > 255)
            {
                throw new NotImplementedException("The actual encoding for longer strings is more complicated");
            }

            return new byte[] { (byte)Encoding.UTF8.GetByteCount(s) }.Concat(Encoding.UTF8.GetBytes(s)).ToArray();
        }

        private static BinaryPlaceholderListDatabase CreatePlaceholderListDatabase(ConfigurableFileSystem fs, byte[] initialContents)
        {
            fs.ExpectedFiles.Add(MockEntryFileName, new ReusableMemoryStream(initialContents));

            string error;
            BinaryPlaceholderListDatabase dut;
            BinaryPlaceholderListDatabase.TryCreate(null, MockEntryFileName, fs, out dut, out error).ShouldEqual(true, error);
            dut.ShouldNotBeNull();
            return dut;
        }
    }
}
