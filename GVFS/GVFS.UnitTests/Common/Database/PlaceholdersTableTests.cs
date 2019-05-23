using GVFS.Common.Database;
using GVFS.Tests.Should;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace GVFS.UnitTests.Common.Database
{
    [TestFixture]
    public class PlaceholdersTableTests
    {
        private const string DefaultPath = "test";
        private const byte PathTypeFile = 0;
        private const byte PathTypePartialFolder = 1;
        private const byte PathTypeExpandedFolder = 2;
        private const byte PathTypePossibleTombstoneFolder = 3;
        private const string DefaultSha = "SHA1";

        [TestCase]
        public void ConstructorTest()
        {
            Mock<IGVFSConnectionPool> mockConnectionPool = new Mock<IGVFSConnectionPool>(MockBehavior.Strict);
            PlaceholdersTable placeholders = new PlaceholdersTable(mockConnectionPool.Object);
            mockConnectionPool.VerifyAll();
        }

        [TestCase]
        public void CreateTableTest()
        {
            Mock<IDbCommand> mockCommand = new Mock<IDbCommand>(MockBehavior.Strict);
            mockCommand.SetupSet(x => x.CommandText = "CREATE TABLE IF NOT EXISTS [Placeholders] (path TEXT PRIMARY KEY, pathType TINYINT NOT NULL, sha char(40) ) WITHOUT ROWID;");
            mockCommand.Setup(x => x.ExecuteNonQuery()).Returns(1);
            PlaceholdersTable.CreateTable(mockCommand.Object);
            mockCommand.VerifyAll();
        }

        [TestCase]
        public void CountTest()
        {
            this.TestPlaceholders(
                (placeholders, mockCommand) =>
                {
                    mockCommand.SetupSet(x => x.CommandText = "SELECT count(path) FROM Placeholders;");
                    mockCommand.Setup(x => x.ExecuteScalar()).Returns(0);
                    placeholders.Count().ShouldEqual(0);
                });
        }

        [TestCase]
        public void GetAllFilePathsWithNoResults()
        {
            this.TestPlaceholdersWithReader(
               (placeholders, mockCommand, mockReader) =>
               {
                   mockReader.Setup(x => x.Read()).Returns(false);
                   mockCommand.SetupSet(x => x.CommandText = "SELECT path FROM Placeholders WHERE pathType = 0;");

                   HashSet<string> filePaths = placeholders.GetAllFilePaths();
                   filePaths.ShouldNotBeNull();
                   filePaths.Count.ShouldEqual(0);
               });
        }

        [TestCase]
        public void GetAllFilePathsTest()
        {
            this.TestPlaceholdersWithReader(
               (placeholders, mockCommand, mockReader) =>
               {
                   int readCalls = 0;
                   mockReader.Setup(x => x.Read()).Returns(() =>
                   {
                       ++readCalls;
                       return readCalls == 1;
                   });

                   mockReader.Setup(x => x.GetString(0)).Returns(DefaultPath);
                   mockCommand.SetupSet(x => x.CommandText = "SELECT path FROM Placeholders WHERE pathType = 0;");

                   HashSet<string> filePaths = placeholders.GetAllFilePaths();
                   filePaths.ShouldNotBeNull();
                   filePaths.Count.ShouldEqual(1);
                   filePaths.ShouldContain(x => x == DefaultPath);
               });
        }

        [TestCase]
        public void GetAllEntriesReturnsNothing()
        {
            List<PlaceholdersTable.PlaceholderData> expectedPlacholders = new List<PlaceholdersTable.PlaceholderData>();
            this.TestPlaceholdersWithReader(
               (placeholders, mockCommand, mockReader) =>
               {
                   this.SetupMockReader(mockReader, expectedPlacholders);
                   mockCommand.SetupSet(x => x.CommandText = "SELECT path, pathType, sha FROM Placeholders;");

                   placeholders.GetAllEntries(out List<IPlaceholderData> filePlaceholders, out List<IPlaceholderData> folderPlaceholders);
                   filePlaceholders.ShouldNotBeNull();
                   filePlaceholders.Count.ShouldEqual(0);

                   folderPlaceholders.ShouldNotBeNull();
                   folderPlaceholders.Count.ShouldEqual(0);
               });
        }

        [TestCase]
        public void GetAllEntriesReturnsOneFile()
        {
            List<PlaceholdersTable.PlaceholderData> expectedPlacholders = new List<PlaceholdersTable.PlaceholderData>();
            expectedPlacholders.Add(new PlaceholdersTable.PlaceholderData() { Path = DefaultPath, PathType = PlaceholdersTable.PlaceholderData.PlaceholderType.File, Sha = DefaultSha });
            this.TestPlaceholdersWithReader(
               (placeholders, mockCommand, mockReader) =>
               {
                   this.SetupMockReader(mockReader, expectedPlacholders);
                   mockCommand.SetupSet(x => x.CommandText = "SELECT path, pathType, sha FROM Placeholders;");

                   placeholders.GetAllEntries(out List<IPlaceholderData> filePlaceholders, out List<IPlaceholderData> folderPlaceholders);
                   filePlaceholders.ShouldNotBeNull();
                   this.PlaceholderListShouldMatch(expectedPlacholders, filePlaceholders);

                   folderPlaceholders.ShouldNotBeNull();
                   folderPlaceholders.Count.ShouldEqual(0);
               });
        }

        [TestCase]
        public void GetAllEntriesReturnsOneFolder()
        {
            List<PlaceholdersTable.PlaceholderData> expectedPlacholders = new List<PlaceholdersTable.PlaceholderData>();
            expectedPlacholders.Add(new PlaceholdersTable.PlaceholderData() { Path = DefaultPath, PathType = PlaceholdersTable.PlaceholderData.PlaceholderType.PartialFolder, Sha = null });
            this.TestPlaceholdersWithReader(
               (placeholders, mockCommand, mockReader) =>
               {
                   this.SetupMockReader(mockReader, expectedPlacholders);
                   mockCommand.SetupSet(x => x.CommandText = "SELECT path, pathType, sha FROM Placeholders;");

                   placeholders.GetAllEntries(out List<IPlaceholderData> filePlaceholders, out List<IPlaceholderData> folderPlaceholders);
                   filePlaceholders.ShouldNotBeNull();
                   filePlaceholders.Count.ShouldEqual(0);

                   folderPlaceholders.ShouldNotBeNull();
                   this.PlaceholderListShouldMatch(expectedPlacholders, folderPlaceholders);
               });
        }

        [TestCase]
        public void GetAllEntriesReturnsMultiple()
        {
            List<PlaceholdersTable.PlaceholderData> expectedFilePlacholders = new List<PlaceholdersTable.PlaceholderData>();
            expectedFilePlacholders.Add(new PlaceholdersTable.PlaceholderData() { Path = DefaultPath, PathType = PlaceholdersTable.PlaceholderData.PlaceholderType.File, Sha = DefaultSha });
            List<PlaceholdersTable.PlaceholderData> expectedFolderPlacholders = new List<PlaceholdersTable.PlaceholderData>();
            expectedFolderPlacholders.Add(new PlaceholdersTable.PlaceholderData() { Path = "test1", PathType = PlaceholdersTable.PlaceholderData.PlaceholderType.PartialFolder, Sha = null });
            expectedFolderPlacholders.Add(new PlaceholdersTable.PlaceholderData() { Path = "test2", PathType = PlaceholdersTable.PlaceholderData.PlaceholderType.ExpandedFolder, Sha = null });
            expectedFolderPlacholders.Add(new PlaceholdersTable.PlaceholderData() { Path = "test3", PathType = PlaceholdersTable.PlaceholderData.PlaceholderType.PossibleTombstoneFolder, Sha = null });
            this.TestPlaceholdersWithReader(
               (placeholders, mockCommand, mockReader) =>
               {
                   this.SetupMockReader(mockReader, expectedFilePlacholders.Union(expectedFolderPlacholders).ToList());
                   mockCommand.SetupSet(x => x.CommandText = "SELECT path, pathType, sha FROM Placeholders;");

                   placeholders.GetAllEntries(out List<IPlaceholderData> filePlaceholders, out List<IPlaceholderData> folderPlaceholders);
                   filePlaceholders.ShouldNotBeNull();
                   this.PlaceholderListShouldMatch(expectedFilePlacholders, filePlaceholders);

                   folderPlaceholders.ShouldNotBeNull();
                   this.PlaceholderListShouldMatch(expectedFolderPlacholders, folderPlaceholders);
               });
        }

        [TestCase]
        public void AddPlaceholderDataWithFile()
        {
            PlaceholdersTable.PlaceholderData placeholderData = new PlaceholdersTable.PlaceholderData()
            {
                Path = DefaultPath,
                PathType = PlaceholdersTable.PlaceholderData.PlaceholderType.File,
                Sha = DefaultSha
            };

            this.TestPlaceholdersInsert(
                placeholders => placeholders.AddPlaceholderData(placeholderData),
                DefaultPath,
                PathTypeFile,
                DefaultSha);
        }

        [TestCase]
        public void AddPlaceholderDataWithPartialFolder()
        {
            PlaceholdersTable.PlaceholderData placeholderData = new PlaceholdersTable.PlaceholderData()
            {
                Path = DefaultPath,
                PathType = PlaceholdersTable.PlaceholderData.PlaceholderType.PartialFolder,
                Sha = null
            };

            this.TestPlaceholdersInsert(
                placeholders => placeholders.AddPlaceholderData(placeholderData),
                DefaultPath,
                PathTypePartialFolder,
                sha: null);
        }

        [TestCase]
        public void AddPlaceholderDataWithExpandedFolder()
        {
            PlaceholdersTable.PlaceholderData placeholderData = new PlaceholdersTable.PlaceholderData()
            {
                Path = DefaultPath,
                PathType = PlaceholdersTable.PlaceholderData.PlaceholderType.ExpandedFolder,
                Sha = null
            };

            this.TestPlaceholdersInsert(
                placeholders => placeholders.AddPlaceholderData(placeholderData),
                DefaultPath,
                PathTypeExpandedFolder,
                sha: null);
        }

        [TestCase]
        public void AddPlaceholderDataWithPossibleTombstoneFolder()
        {
            PlaceholdersTable.PlaceholderData placeholderData = new PlaceholdersTable.PlaceholderData()
            {
                Path = DefaultPath,
                PathType = PlaceholdersTable.PlaceholderData.PlaceholderType.PossibleTombstoneFolder,
                Sha = null
            };

            this.TestPlaceholdersInsert(
                placeholders => placeholders.AddPlaceholderData(placeholderData),
                DefaultPath,
                PathTypePossibleTombstoneFolder,
                sha: null);
        }

        [TestCase]
        public void AddFileTest()
        {
            this.TestPlaceholdersInsert(
                placeholders => placeholders.AddFile(DefaultPath, DefaultSha),
                DefaultPath,
                PathTypeFile,
                DefaultSha);
        }

        [TestCase]
        public void AddPartialFolder()
        {
            this.TestPlaceholdersInsert(
                placeholders => placeholders.AddPartialFolder(DefaultPath),
                DefaultPath,
                PathTypePartialFolder,
                sha: null);
        }

        [TestCase]
        public void AddExpandedFolder()
        {
            this.TestPlaceholdersInsert(
                placeholders => placeholders.AddExpandedFolder(DefaultPath),
                DefaultPath,
                PathTypeExpandedFolder,
                sha: null);
        }

        [TestCase]
        public void AddPossibleTombstoneFolder()
        {
            this.TestPlaceholdersInsert(
                placeholders => placeholders.AddPossibleTombstoneFolder(DefaultPath),
                DefaultPath,
                PathTypePossibleTombstoneFolder,
                sha: null);
        }

        [TestCase]
        public void RemoveTest()
        {
            this.TestPlaceholders(
                (placeholders, mockCommand) =>
                {
                    Mock<IDbDataParameter> mockParameter = new Mock<IDbDataParameter>(MockBehavior.Strict);
                    mockParameter.SetupSet(x => x.ParameterName = "@path");
                    mockParameter.SetupSet(x => x.DbType = DbType.String);
                    mockParameter.SetupSet(x => x.Value = DefaultPath);

                    Mock<IDataParameterCollection> mockParameters = new Mock<IDataParameterCollection>(MockBehavior.Strict);
                    mockParameters.Setup(x => x.Add(mockParameter.Object)).Returns(0);

                    mockCommand.SetupSet(x => x.CommandText = "DELETE FROM Placeholders WHERE path = @path;");
                    mockCommand.Setup(x => x.CreateParameter()).Returns(mockParameter.Object);
                    mockCommand.SetupGet(x => x.Parameters).Returns(mockParameters.Object);
                    mockCommand.Setup(x => x.ExecuteNonQuery()).Returns(1);

                    placeholders.Remove(DefaultPath);

                    mockParameters.VerifyAll();
                    mockParameter.VerifyAll();
                });
        }

        private void TestPlaceholdersInsert(Action<PlaceholdersTable> testCode, string path, int pathType, string sha)
        {
            this.TestPlaceholders(
                (placeholders, mockCommand) =>
                {
                    Mock<IDbDataParameter> mockPathParameter = new Mock<IDbDataParameter>(MockBehavior.Strict);
                    mockPathParameter.SetupSet(x => x.ParameterName = "@path");
                    mockPathParameter.SetupSet(x => x.DbType = DbType.String);
                    mockPathParameter.SetupSet(x => x.Value = path);
                    Mock<IDbDataParameter> mockPathTypeParameter = new Mock<IDbDataParameter>(MockBehavior.Strict);
                    mockPathTypeParameter.SetupSet(x => x.ParameterName = "@pathType");
                    mockPathTypeParameter.SetupSet(x => x.DbType = DbType.Int32);
                    mockPathTypeParameter.SetupSet(x => x.Value = pathType);
                    Mock<IDbDataParameter> mockShaParameter = new Mock<IDbDataParameter>(MockBehavior.Strict);
                    mockShaParameter.SetupSet(x => x.ParameterName = "@sha");
                    mockShaParameter.SetupSet(x => x.DbType = DbType.String);
                    if (sha == null)
                    {
                        mockShaParameter.SetupSet(x => x.Value = DBNull.Value);
                    }
                    else
                    {
                        mockShaParameter.SetupSet(x => x.Value = sha);
                    }

                    Mock<IDataParameterCollection> mockParameters = new Mock<IDataParameterCollection>(MockBehavior.Strict);
                    mockParameters.Setup(x => x.Add(mockPathParameter.Object)).Returns(0);
                    mockParameters.Setup(x => x.Add(mockPathTypeParameter.Object)).Returns(0);
                    mockParameters.Setup(x => x.Add(mockShaParameter.Object)).Returns(0);

                    mockCommand.Setup(x => x.CreateParameter()).Returns(mockPathParameter.Object);
                    mockCommand.SetupSequence(x => x.CreateParameter())
                        .Returns(mockPathParameter.Object)
                        .Returns(mockPathTypeParameter.Object)
                        .Returns(mockShaParameter.Object);
                    mockCommand.SetupGet(x => x.Parameters).Returns(mockParameters.Object);

                    mockCommand.SetupSet(x => x.CommandText = "INSERT OR REPLACE INTO Placeholders (path, pathType, sha) VALUES (@path, @pathType, @sha);");
                    mockCommand.Setup(x => x.ExecuteNonQuery()).Returns(1);

                    testCode(placeholders);

                    mockParameters.VerifyAll();
                    mockPathParameter.VerifyAll();
                    mockPathTypeParameter.VerifyAll();
                    mockShaParameter.VerifyAll();
                });
        }

        private void TestPlaceholdersWithReader(Action<PlaceholdersTable, Mock<IDbCommand>, Mock<IDataReader>> testCode)
        {
            this.TestPlaceholders(
                (placeholders, mockCommand) =>
                {
                    Mock<IDataReader> mockReader = new Mock<IDataReader>(MockBehavior.Strict);
                    mockReader.Setup(x => x.Dispose());

                    mockCommand.Setup(x => x.ExecuteReader()).Returns(mockReader.Object);
                    testCode(placeholders, mockCommand, mockReader);
                    mockReader.VerifyAll();
                });
        }

        private void TestPlaceholders(Action<PlaceholdersTable, Mock<IDbCommand>> testCode)
        {
            Mock<IDbCommand> mockCommand = new Mock<IDbCommand>(MockBehavior.Strict);
            mockCommand.Setup(x => x.Dispose());

            Mock<IDbConnection> mockConnection = new Mock<IDbConnection>(MockBehavior.Strict);
            mockConnection.Setup(x => x.CreateCommand()).Returns(mockCommand.Object);
            mockConnection.Setup(x => x.Dispose());

            Mock<IGVFSConnectionPool> mockConnectionPool = new Mock<IGVFSConnectionPool>(MockBehavior.Strict);
            mockConnectionPool.Setup(x => x.GetConnection()).Returns(mockConnection.Object);

            PlaceholdersTable placeholders = new PlaceholdersTable(mockConnectionPool.Object);
            testCode(placeholders, mockCommand);

            mockCommand.VerifyAll();
            mockConnection.VerifyAll();
            mockConnectionPool.VerifyAll();
        }

        private void SetupMockReader(Mock<IDataReader> mockReader, List<PlaceholdersTable.PlaceholderData> data)
        {
            int readCalls = -1;
            mockReader.Setup(x => x.Read()).Returns(() =>
            {
                ++readCalls;
                return readCalls < data.Count;
            });

            if (data.Count > 0)
            {
                mockReader.Setup(x => x.GetString(0)).Returns(() => data[readCalls].Path);
                mockReader.Setup(x => x.GetByte(1)).Returns(() => (byte)data[readCalls].PathType);
                mockReader.Setup(x => x.IsDBNull(2)).Returns(() => data[readCalls].Sha == null);

                if (data.Any(x => !x.IsFolder))
                {
                    mockReader.Setup(x => x.GetString(2)).Returns(() => data[readCalls].Sha);
                }
            }
        }

        private void PlaceholderListShouldMatch(IReadOnlyList<IPlaceholderData> expected, IReadOnlyList<IPlaceholderData> actual)
        {
            actual.Count.ShouldEqual(expected.Count);

            for (int i = 0; i < actual.Count; i++)
            {
                this.PlaceholderDataShouldMatch(expected[i], actual[i]);
            }
        }

        private void PlaceholderDataShouldMatch(IPlaceholderData expected, IPlaceholderData actual)
        {
            actual.Path.ShouldEqual(expected.Path);
            actual.IsFolder.ShouldEqual(expected.IsFolder);
            actual.IsExpandedFolder.ShouldEqual(expected.IsExpandedFolder);
            actual.IsPossibleTombstoneFolder.ShouldEqual(expected.IsPossibleTombstoneFolder);
            actual.Sha.ShouldEqual(expected.Sha);
        }
    }
}
