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
                using (IPooledConnection pooledConnection1 = connectionPool.GetConnection())
                using (IPooledConnection pooledConnection2 = connectionPool.GetConnection())
                {
                    pooledConnection1.Connection.Equals(pooledConnection2.Connection).ShouldBeFalse();
                }
            });
        }

        [TestCase]
        public void DisposedConnectionReturnsToPoolTest()
        {
            this.TestGVFSDatabase(database =>
            {
                IGVFSConnectionPool connectionPool = database;
                IPooledConnection pooledConnection = connectionPool.GetConnection();
                IDbConnection connection = pooledConnection.Connection;
                pooledConnection.Dispose();
                pooledConnection = connectionPool.GetConnection();
                connection.Equals(pooledConnection.Connection).ShouldBeTrue();
                pooledConnection.Dispose();
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
            mockCommand.SetupSet(x => x.CommandText = "CREATE TABLE IF NOT EXISTS [Placeholders] (path TEXT PRIMARY KEY, pathType TINYINT NOT NULL, sha char(40) ) WITHOUT ROWID;");
            mockCommand.Setup(x => x.ExecuteNonQuery()).Returns(1);
            mockCommand.Setup(x => x.ExecuteScalar()).Returns(1);
            mockCommand.Setup(x => x.Dispose());

            List<Mock<IDbConnection>> mockConnections = new List<Mock<IDbConnection>>();
            Mock<IDbConnection> mockConnection = new Mock<IDbConnection>(MockBehavior.Strict);
            mockConnection.Setup(x => x.CreateCommand()).Returns(mockCommand.Object);
            mockConnection.Setup(x => x.Dispose());
            mockConnections.Add(mockConnection);

            Mock<IDbConnectionCreator> mockConnectionCreator = new Mock<IDbConnectionCreator>(MockBehavior.Strict);
            bool firstConnection = true;
            mockConnectionCreator.Setup(x => x.OpenNewConnection(@"mock:root\.gvfs\databases\gvfs.sqlite")).Returns(() =>
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

            using (GVFSDatabase database = new GVFSDatabase(new MockTracer(), fileSystem, "mock:root", mockConnectionCreator.Object, initialPooledConnections: 1))
            {
                testCode?.Invoke(database);
            }

            mockCommand.Verify(x => x.Dispose(), Times.Once);
            mockConnections.ForEach(connection => connection.Verify(x => x.Dispose(), Times.Once));

            mockCommand.VerifyAll();
            mockConnections.ForEach(connection => connection.VerifyAll());
            mockConnectionCreator.VerifyAll();
        }
    }
}
