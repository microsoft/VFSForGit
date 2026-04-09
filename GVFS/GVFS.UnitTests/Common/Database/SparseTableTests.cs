using GVFS.Common.Database;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;

namespace GVFS.UnitTests.Common.Database
{
    [TestFixture]
    public class SparseTableTests : TableTests<SparseTable>
    {
        private const string GetAllCommandString = "SELECT path FROM Sparse;";
        private const string RemoveCommandString = "DELETE FROM Sparse WHERE path = @path;";
        private const string AddCommandString = "INSERT OR REPLACE INTO Sparse (path) VALUES (@path);";
        private static readonly string DefaultFolderPath = Path.Combine("GVFS", "GVFS");

        private static PathData[] pathsToTest = new[]
        {
            new PathData("GVFS", "GVFS"),
            new PathData(CombineAlt("GVFS", "GVFS"), Path.Combine("GVFS", "GVFS")),
            new PathData(CombineAltForTrim(Path.AltDirectorySeparatorChar, "GVFS", "GVFS"), Path.Combine("GVFS", "GVFS")),
            new PathData(CombineAltForTrim(Path.DirectorySeparatorChar, "GVFS", "GVFS"), Path.Combine("GVFS", "GVFS")),
            new PathData(CombineAltForTrim(' ', "GVFS", "GVFS"), Path.Combine("GVFS", "GVFS")),
            new PathData(CombineAltForTrim('\r', "GVFS", "GVFS"), Path.Combine("GVFS", "GVFS")),
            new PathData(CombineAltForTrim('\n', "GVFS", "GVFS"), Path.Combine("GVFS", "GVFS")),
            new PathData(CombineAltForTrim('\t', "GVFS", "GVFS"), Path.Combine("GVFS", "GVFS")),
        };

        protected override string CreateTableCommandString => "CREATE TABLE IF NOT EXISTS [Sparse] (path TEXT PRIMARY KEY COLLATE NOCASE) WITHOUT ROWID;";

        [TestCase]
        public void GetAllWithNoResults()
        {
            this.TestTableWithReader((sparseTable, mockCommand, mockReader) =>
            {
                mockReader.Setup(x => x.Read()).Returns(false);
                mockCommand.SetupSet(x => x.CommandText = GetAllCommandString);

                HashSet<string> sparseEntries = sparseTable.GetAll();
                sparseEntries.Count.ShouldEqual(0);
            });
        }

        [TestCase]
        public void GetAllWithWithOneResult()
        {
            this.TestTableWithReader((sparseTable, mockCommand, mockReader) =>
            {
                mockCommand.SetupSet(x => x.CommandText = GetAllCommandString);
                this.SetupMockReader(mockReader, DefaultFolderPath);

                HashSet<string> sparseEntries = sparseTable.GetAll();
                sparseEntries.Count.ShouldEqual(1);
                sparseEntries.First().ShouldEqual(DefaultFolderPath);
            });
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void GetAllThrowsGVFSDatabaseException()
        {
            this.TestTableWithReader(
                (sparseTable, mockCommand, mockReader) =>
                {
                    mockCommand.SetupSet(x => x.CommandText = GetAllCommandString);
                    mockReader.Setup(x => x.Read()).Throws(new Exception(DefaultExceptionMessage));
                    GVFSDatabaseException ex = Assert.Throws<GVFSDatabaseException>(() => sparseTable.GetAll());
                    ex.Message.ShouldContain("SparseTable.GetAll Exception:");
                    ex.InnerException.Message.ShouldEqual(DefaultExceptionMessage);
                });
        }

        [TestCase]
        public void AddVariousPaths()
        {
            foreach (PathData pathData in pathsToTest)
            {
                this.TestSparseTableAddOrRemove(
                    isAdd: true,
                    pathToPass: pathData.PathToPassMethod,
                    expectedPath: pathData.ExpectedPathInTable);
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void AddThrowsGVFSDatabaseException()
        {
            this.TestSparseTableAddOrRemove(
                isAdd: true,
                pathToPass: DefaultFolderPath,
                expectedPath: DefaultFolderPath,
                throwException: true);
        }

        [TestCase]
        public void RemoveVariousPaths()
        {
            foreach (PathData pathData in pathsToTest)
            {
                this.TestSparseTableAddOrRemove(
                    isAdd: false,
                    pathToPass: pathData.PathToPassMethod,
                    expectedPath: pathData.ExpectedPathInTable);
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void RemoveThrowsGVFSDatabaseException()
        {
            this.TestSparseTableAddOrRemove(
                isAdd: false,
                pathToPass: DefaultFolderPath,
                expectedPath: DefaultFolderPath,
                throwException: true);
        }

        protected override SparseTable TableFactory(IGVFSConnectionPool pool)
        {
            return new SparseTable(pool);
        }

        protected override void CreateTable(IDbConnection connection, bool caseSensitiveFileSystem)
        {
            SparseTable.CreateTable(connection, caseSensitiveFileSystem);
        }

        private static string CombineAltForTrim(char character, params string[] folders)
        {
            return $"{character}{CombineAlt(folders)}{character}";
        }

        private static string CombineAlt(params string[] folders)
        {
            return string.Join(Path.AltDirectorySeparatorChar.ToString(), folders);
        }

        private void SetupMockReader(Mock<IDataReader> mockReader, params string[] data)
        {
            int readCalls = -1;
            mockReader.Setup(x => x.Read()).Returns(() =>
            {
                ++readCalls;
                return readCalls < data.Length;
            });

            if (data.Length > 0)
            {
                mockReader.Setup(x => x.GetString(0)).Returns(() => data[readCalls]);
            }
        }

        private void TestSparseTableAddOrRemove(bool isAdd, string pathToPass, string expectedPath, bool throwException = false)
        {
            this.TestTable(
                (sparseTable, mockCommand) =>
                {
                    Mock<IDbDataParameter> mockParameter = new Mock<IDbDataParameter>(MockBehavior.Strict);
                    mockParameter.SetupSet(x => x.ParameterName = "@path");
                    mockParameter.SetupSet(x => x.DbType = DbType.String);
                    mockParameter.SetupSet(x => x.Value = expectedPath);

                    Mock<IDataParameterCollection> mockParameters = new Mock<IDataParameterCollection>(MockBehavior.Strict);
                    mockParameters.Setup(x => x.Add(mockParameter.Object)).Returns(0);

                    mockCommand.Setup(x => x.CreateParameter()).Returns(mockParameter.Object);
                    mockCommand.SetupGet(x => x.Parameters).Returns(mockParameters.Object);
                    if (throwException)
                    {
                        mockCommand.Setup(x => x.ExecuteNonQuery()).Throws(new Exception(DefaultExceptionMessage));
                    }
                    else
                    {
                        mockCommand.Setup(x => x.ExecuteNonQuery()).Returns(1);
                    }

                    if (isAdd)
                    {
                        mockCommand.SetupSet(x => x.CommandText = AddCommandString);
                        if (throwException)
                        {
                            GVFSDatabaseException ex = Assert.Throws<GVFSDatabaseException>(() => sparseTable.Add(pathToPass));
                            ex.Message.ShouldContain($"SparseTable.Add({expectedPath}) Exception");
                            ex.InnerException.Message.ShouldEqual(DefaultExceptionMessage);
                        }
                        else
                        {
                            sparseTable.Add(pathToPass);
                        }
                    }
                    else
                    {
                        mockCommand.SetupSet(x => x.CommandText = RemoveCommandString);
                        if (throwException)
                        {
                            GVFSDatabaseException ex = Assert.Throws<GVFSDatabaseException>(() => sparseTable.Remove(pathToPass));
                            ex.Message.ShouldContain($"SparseTable.Remove({expectedPath}) Exception");
                            ex.InnerException.Message.ShouldEqual(DefaultExceptionMessage);
                        }
                        else
                        {
                            sparseTable.Remove(pathToPass);
                        }
                    }

                    mockParameters.VerifyAll();
                    mockParameter.VerifyAll();
                });
        }

        private class PathData
        {
            public PathData(string path, string expected)
            {
                this.PathToPassMethod = path;
                this.ExpectedPathInTable = expected;
            }

            public string PathToPassMethod { get; }
            public string ExpectedPathInTable { get; }
        }
    }
}
