using GVFS.Common.Database;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.FileSystem;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;

namespace GVFS.UnitTests.Common.Database
{
    [TestFixture]
    public class GVFSDatabaseTests
    {
        [TestCase]
        public void ConstructorTest()
        {
            this.TestGVFSDatabase(null);
        }

        [TestCase]
        public void DisposeTest()
        {
            this.TestGVFSDatabase(database => database.Dispose());
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void GetConnectionAfterDisposeShouldThrowException()
        {
            this.TestGVFSDatabase(database =>
                {
                    database.Dispose();
                    IGVFSConnectionPool connectionPool = database;
                    Assert.Throws<ObjectDisposedException>(() => connectionPool.GetConnection());
                });
        }

        [TestCase]
        public void GetConnectionMoreThanInPoolTest()
        {
            this.TestGVFSDatabase(database =>
            {
                IGVFSConnectionPool connectionPool = database;
                using (IDbConnection pooledConnection1 = connectionPool.GetConnection())
                using (IDbConnection pooledConnection2 = connectionPool.GetConnection())
                {
                    pooledConnection1.Equals(pooledConnection2).ShouldBeFalse();
                }
            });
        }

        private void TestGVFSDatabase(Action<GVFSDatabase> testCode)
        {
            MockFileSystem fileSystem = new MockFileSystem(new MockDirectory("GVFSDatabaseTests", null, null));

            Mock<IDbCommand> mockCommand = new Mock<IDbCommand>(MockBehavior.Strict);
            mockCommand.SetupSet(x => x.CommandText = "PRAGMA journal_mode=WAL;");
            mockCommand.SetupSet(x => x.CommandText = "PRAGMA cache_size=-40000;");
            mockCommand.SetupSet(x => x.CommandText = "PRAGMA synchronous=NORMAL;");
            mockCommand.SetupSet(x => x.CommandText = "PRAGMA user_version;");
            mockCommand.SetupSet(x => x.CommandText = "CREATE TABLE IF NOT EXISTS [Placeholder] (path TEXT PRIMARY KEY, pathType TINYINT NOT NULL, sha char(40) ) WITHOUT ROWID;");
            mockCommand.Setup(x => x.ExecuteNonQuery()).Returns(1);
            mockCommand.Setup(x => x.ExecuteScalar()).Returns(1);
            mockCommand.Setup(x => x.Dispose());

            List<Mock<IDbConnection>> mockConnections = new List<Mock<IDbConnection>>();
            Mock<IDbConnection> mockConnection = new Mock<IDbConnection>(MockBehavior.Strict);
            mockConnection.Setup(x => x.CreateCommand()).Returns(mockCommand.Object);
            mockConnection.Setup(x => x.Dispose());
            mockConnections.Add(mockConnection);

            Mock<IDbConnectionFactory> mockConnectionFactory = new Mock<IDbConnectionFactory>(MockBehavior.Strict);
            bool firstConnection = true;
            string databasePath = Path.Combine("mock:root", ".gvfs", "databases", "VFSForGit.sqlite");
            mockConnectionFactory.Setup(x => x.OpenNewConnection(databasePath)).Returns(() =>
            {
                if (firstConnection)
                {
                    firstConnection = false;
                    return mockConnection.Object;
                }
                else
                {
                    Mock<IDbConnection> newMockConnection = new Mock<IDbConnection>(MockBehavior.Strict);
                    newMockConnection.Setup(x => x.Dispose());
                    mockConnections.Add(newMockConnection);
                    return newMockConnection.Object;
                }
            });

            using (GVFSDatabase database = new GVFSDatabase(fileSystem, "mock:root", mockConnectionFactory.Object, initialPooledConnections: 1))
            {
                testCode?.Invoke(database);
            }

            mockCommand.Verify(x => x.Dispose(), Times.Once);
            mockConnections.ForEach(connection => connection.Verify(x => x.Dispose(), Times.Once));

            mockCommand.VerifyAll();
            mockConnections.ForEach(connection => connection.VerifyAll());
            mockConnectionFactory.VerifyAll();
        }
    }
}
