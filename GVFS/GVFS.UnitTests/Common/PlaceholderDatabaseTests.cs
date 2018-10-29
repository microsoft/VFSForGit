using GVFS.Common;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock;
using GVFS.UnitTests.Mock.FileSystem;
using NUnit.Framework;
using System.Collections.Generic;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class PlaceholderDatabaseTests
    {
        private const string MockEntryFileName = "mock:\\entries.dat";

        private const string InputGitIgnorePath = ".gitignore";
        private const string InputGitIgnoreSHA = "AE930E4CF715315FC90D4AEC98E16A7398F8BF64";

        private const string InputGitAttributesPath = ".gitattributes";
        private const string InputGitAttributesSHA = "BB9630E4CF715315FC90D4AEC98E167398F8BF66";

        private const string InputThirdFilePath = "thirdFile";
        private const string InputThirdFileSHA = "ff9630E00F715315FC90D4AEC98E6A7398F8BF11";

        private const string PlaceholderDatabaseNewLine = "\r\n";
        private const string ExpectedGitIgnoreEntry = "A " + InputGitIgnorePath + "\0" + InputGitIgnoreSHA + PlaceholderDatabaseNewLine;
        private const string ExpectedGitAttributesEntry = "A " + InputGitAttributesPath + "\0" + InputGitAttributesSHA + PlaceholderDatabaseNewLine;

        private const string ExpectedTwoEntries = ExpectedGitIgnoreEntry + ExpectedGitAttributesEntry;

        [TestCase]
        public void ParsesExistingDataCorrectly()
        {
            ConfigurableFileSystem fs = new ConfigurableFileSystem();
            PlaceholderListDatabase dut = CreatePlaceholderListDatabase(
                fs,
                "A .gitignore\0AE930E4CF715315FC90D4AEC98E16A7398F8BF64\r\n" +
                "A Test_EPF_UpdatePlaceholderTests\\LockToPreventDelete\\test.txt\0B6948308A8633CC1ED94285A1F6BF33E35B7C321\r\n" +
                "A Test_EPF_UpdatePlaceholderTests\\LockToPreventDelete\\test.txt\0C7048308A8633CC1ED94285A1F6BF33E35B7C321\r\n" +
                "A Test_EPF_UpdatePlaceholderTests\\LockToPreventDelete\\test2.txt\0D19198D6EA60F0D66F0432FEC6638D0A73B16E81\r\n" +
                "A Test_EPF_UpdatePlaceholderTests\\LockToPreventDelete\\test3.txt\0E45EA0D328E581696CAF1F823686F3665A5F05C1\r\n" +
                "A Test_EPF_UpdatePlaceholderTests\\LockToPreventDelete\\test4.txt\0FCB3E2C561649F102DD8110A87DA82F27CC05833\r\n" +
                "A Test_EPF_UpdatePlaceholderTests\\LockToPreventUpdate\\test.txt\0E51B377C95076E4C6A9E22A658C5690F324FD0AD\r\n" +
                "D Test_EPF_UpdatePlaceholderTests\\LockToPreventUpdate\\test.txt\r\n" +
                "D Test_EPF_UpdatePlaceholderTests\\LockToPreventUpdate\\test.txt\r\n" +
                "D Test_EPF_UpdatePlaceholderTests\\LockToPreventUpdate\\test.txt\r\n");
            dut.EstimatedCount.ShouldEqual(5);
        }

        [TestCase]
        public void WritesPlaceholderAddToFile()
        {
            ConfigurableFileSystem fs = new ConfigurableFileSystem();
            PlaceholderListDatabase dut = CreatePlaceholderListDatabase(fs, string.Empty);
            dut.AddAndFlushFile(InputGitIgnorePath, InputGitIgnoreSHA);

            fs.ExpectedFiles[MockEntryFileName].ReadAsString().ShouldEqual(ExpectedGitIgnoreEntry);

            dut.AddAndFlushFile(InputGitAttributesPath, InputGitAttributesSHA);

            fs.ExpectedFiles[MockEntryFileName].ReadAsString().ShouldEqual(ExpectedTwoEntries);
        }

        [TestCase]
        public void GetAllEntriesAndPrepToWriteAllEntriesReturnsCorrectEntries()
        {
            ConfigurableFileSystem fs = new ConfigurableFileSystem();
            using (PlaceholderListDatabase dut1 = CreatePlaceholderListDatabase(fs, string.Empty))
            {
                dut1.AddAndFlushFile(InputGitIgnorePath, InputGitIgnoreSHA);
                dut1.AddAndFlushFile(InputGitAttributesPath, InputGitAttributesSHA);
                dut1.AddAndFlushFile(InputThirdFilePath, InputThirdFileSHA);
                dut1.RemoveAndFlush(InputThirdFilePath);
            }

            string error;
            PlaceholderListDatabase dut2;
            PlaceholderListDatabase.TryCreate(null, MockEntryFileName, fs, out dut2, out error).ShouldEqual(true, error);
            List<PlaceholderListDatabase.PlaceholderData> allData = dut2.GetAllEntriesAndPrepToWriteAllEntries();
            allData.Count.ShouldEqual(2);
        }

        [TestCase]
        public void GetAllEntriesAndPrepToWriteAllEntriesSplitsFilesAndFoldersCorrectly()
        {
            ConfigurableFileSystem fs = new ConfigurableFileSystem();
            using (PlaceholderListDatabase dut1 = CreatePlaceholderListDatabase(fs, string.Empty))
            {
                dut1.AddAndFlushFile(InputGitIgnorePath, InputGitIgnoreSHA);
                dut1.AddAndFlushFolder("partialFolder", isExpanded: false);
                dut1.AddAndFlushFile(InputGitAttributesPath, InputGitAttributesSHA);
                dut1.AddAndFlushFolder("expandedFolder", isExpanded: true);
                dut1.AddAndFlushFile(InputThirdFilePath, InputThirdFileSHA);
                dut1.RemoveAndFlush(InputThirdFilePath);
            }

            string error;
            PlaceholderListDatabase dut2;
            PlaceholderListDatabase.TryCreate(null, MockEntryFileName, fs, out dut2, out error).ShouldEqual(true, error);
            List<PlaceholderListDatabase.PlaceholderData> fileData;
            List<PlaceholderListDatabase.PlaceholderData> folderData;
            dut2.GetAllEntriesAndPrepToWriteAllEntries(out fileData, out folderData);
            fileData.Count.ShouldEqual(2);
            folderData.Count.ShouldEqual(2);
            folderData.ShouldContain(
                new[]
                {
                    new PlaceholderListDatabase.PlaceholderData("partialFolder", PlaceholderListDatabase.PartialFolderValue),
                    new PlaceholderListDatabase.PlaceholderData("expandedFolder", PlaceholderListDatabase.ExpandedFolderValue)
                },
                (data1, data2) => data1.Path == data2.Path && data1.Sha == data2.Sha);
        }

        [TestCase]
        public void WriteAllEntriesCorrectlyWritesFile()
        {
            ConfigurableFileSystem fs = new ConfigurableFileSystem();
            fs.ExpectedFiles.Add(MockEntryFileName + ".tmp", new ReusableMemoryStream(string.Empty));

            PlaceholderListDatabase dut = CreatePlaceholderListDatabase(fs, string.Empty);

            List<PlaceholderListDatabase.PlaceholderData> allData = new List<PlaceholderListDatabase.PlaceholderData>()
            {
                new PlaceholderListDatabase.PlaceholderData(InputGitIgnorePath, InputGitIgnoreSHA),
                new PlaceholderListDatabase.PlaceholderData(InputGitAttributesPath, InputGitAttributesSHA)
            };

            dut.WriteAllEntriesAndFlush(allData);
            fs.ExpectedFiles[MockEntryFileName].ReadAsString().ShouldEqual(ExpectedTwoEntries);
        }

        [TestCase]
        public void HandlesRaceBetweenAddAndWriteAllEntries()
        {
            ConfigurableFileSystem fs = new ConfigurableFileSystem();
            fs.ExpectedFiles.Add(MockEntryFileName + ".tmp", new ReusableMemoryStream(string.Empty));

            PlaceholderListDatabase dut = CreatePlaceholderListDatabase(fs, ExpectedGitIgnoreEntry);
            
            List<PlaceholderListDatabase.PlaceholderData> existingEntries = dut.GetAllEntriesAndPrepToWriteAllEntries();

            dut.AddAndFlushFile(InputGitAttributesPath, InputGitAttributesSHA);
            
            dut.WriteAllEntriesAndFlush(existingEntries);
            fs.ExpectedFiles[MockEntryFileName].ReadAsString().ShouldEqual(ExpectedTwoEntries);
        }

        [TestCase]
        public void HandlesRaceBetweenRemoveAndWriteAllEntries()
        {
            const string DeleteGitAttributesEntry = "D .gitattributes" + PlaceholderDatabaseNewLine;

            ConfigurableFileSystem fs = new ConfigurableFileSystem();
            fs.ExpectedFiles.Add(MockEntryFileName + ".tmp", new ReusableMemoryStream(string.Empty));

            PlaceholderListDatabase dut = CreatePlaceholderListDatabase(fs, ExpectedTwoEntries);

            List<PlaceholderListDatabase.PlaceholderData> existingEntries = dut.GetAllEntriesAndPrepToWriteAllEntries();
            
            dut.RemoveAndFlush(InputGitAttributesPath);

            dut.WriteAllEntriesAndFlush(existingEntries);
            fs.ExpectedFiles[MockEntryFileName].ReadAsString().ShouldEqual(ExpectedTwoEntries + DeleteGitAttributesEntry);
        }

        private static PlaceholderListDatabase CreatePlaceholderListDatabase(ConfigurableFileSystem fs, string initialContents)
        {
            fs.ExpectedFiles.Add(MockEntryFileName, new ReusableMemoryStream(initialContents));

            string error;
            PlaceholderListDatabase dut;
            PlaceholderListDatabase.TryCreate(null, MockEntryFileName, fs, out dut, out error).ShouldEqual(true, error);
            dut.ShouldNotBeNull();
            return dut;
        }
    }
}
