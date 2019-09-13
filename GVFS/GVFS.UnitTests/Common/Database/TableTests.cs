using GVFS.Common.Database;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using Moq;
using NUnit.Framework;
using System;
using System.Data;

namespace GVFS.UnitTests.Common.Database
{
    public abstract class TableTests<T>
    {
        protected const string DefaultExceptionMessage = "Somethind bad.";

        protected abstract string CreateTableCommandString { get; }

        [TestCase]
        public void ConstructorTest()
        {
            Mock<IGVFSConnectionPool> mockConnectionPool = new Mock<IGVFSConnectionPool>(MockBehavior.Strict);
            T table = this.TableFactory(mockConnectionPool.Object);
            mockConnectionPool.VerifyAll();
        }

        [TestCase]
        public void CreateTableTest()
        {
            Mock<IDbCommand> mockCommand = new Mock<IDbCommand>(MockBehavior.Strict);
            mockCommand.SetupSet(x => x.CommandText = this.CreateTableCommandString);
            mockCommand.Setup(x => x.ExecuteNonQuery()).Returns(1);
            mockCommand.Setup(x => x.Dispose());

            Mock<IDbConnection> mockConnection = new Mock<IDbConnection>(MockBehavior.Strict);
            mockConnection.Setup(x => x.CreateCommand()).Returns(mockCommand.Object);

            this.CreateTable(mockConnection.Object, caseSensitiveFileSystem: false);
            mockCommand.VerifyAll();
            mockConnection.VerifyAll();
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void CreateTableThrowsExceptionNotWrappedInGVFSDatabaseException()
        {
            Mock<IDbCommand> mockCommand = new Mock<IDbCommand>(MockBehavior.Strict);
            mockCommand.SetupSet(x => x.CommandText = this.CreateTableCommandString);
            mockCommand.Setup(x => x.ExecuteNonQuery()).Throws(new Exception(DefaultExceptionMessage));
            mockCommand.Setup(x => x.Dispose());

            Mock<IDbConnection> mockConnection = new Mock<IDbConnection>(MockBehavior.Strict);
            mockConnection.Setup(x => x.CreateCommand()).Returns(mockCommand.Object);

            Exception ex = Assert.Throws<Exception>(() => this.CreateTable(mockConnection.Object, caseSensitiveFileSystem: false));
            ex.Message.ShouldEqual(DefaultExceptionMessage);
            mockCommand.VerifyAll();
            mockConnection.VerifyAll();
        }

        protected abstract T TableFactory(IGVFSConnectionPool pool);
        protected abstract void CreateTable(IDbConnection connection, bool caseSensitiveFileSystem);

        protected void TestTableWithReader(Action<T, Mock<IDbCommand>, Mock<IDataReader>> testCode)
        {
            this.TestTable(
                (table, mockCommand) =>
                {
                    Mock<IDataReader> mockReader = new Mock<IDataReader>(MockBehavior.Strict);
                    mockReader.Setup(x => x.Dispose());

                    mockCommand.Setup(x => x.ExecuteReader()).Returns(mockReader.Object);
                    testCode(table, mockCommand, mockReader);
                    mockReader.Verify(x => x.Dispose(), Times.Once);
                    mockReader.VerifyAll();
                });
        }

        protected void TestTable(Action<T, Mock<IDbCommand>> testCode)
        {
            Mock<IDbCommand> mockCommand = new Mock<IDbCommand>(MockBehavior.Strict);
            mockCommand.Setup(x => x.Dispose());

            Mock<IDbConnection> mockConnection = new Mock<IDbConnection>(MockBehavior.Strict);
            mockConnection.Setup(x => x.CreateCommand()).Returns(mockCommand.Object);
            mockConnection.Setup(x => x.Dispose());

            Mock<IGVFSConnectionPool> mockConnectionPool = new Mock<IGVFSConnectionPool>(MockBehavior.Strict);
            mockConnectionPool.Setup(x => x.GetConnection()).Returns(mockConnection.Object);

            T table = this.TableFactory(mockConnectionPool.Object);
            testCode(table, mockCommand);

            mockCommand.Verify(x => x.Dispose(), Times.Once);
            mockCommand.VerifyAll();
            mockConnection.Verify(x => x.Dispose(), Times.Once);
            mockConnection.VerifyAll();
            mockConnectionPool.VerifyAll();
        }
    }
}
