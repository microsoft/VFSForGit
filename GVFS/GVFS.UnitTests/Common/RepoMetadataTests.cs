using GVFS.Common;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.FileSystem;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class RepoMetadataTests
    {
        private const string DiskLayoutMajorVersionKey = "DiskLayoutVersion";
        private const string DotGVFSPath = "mock:\\repo\\.gvfs";

        private MockFileSystem fileSystem;

        [SetUp]
        public void SetUp()
        {
            if (GVFSPlatform.Instance == null)
            {
                GVFSPlatform.Register(new MockPlatform());
            }

            MockDirectory root = new MockDirectory("mock:", null, null);
            root.CreateDirectory(Path.Combine(DotGVFSPath, "databases"));
            this.fileSystem = new MockFileSystem(root);

            string error;
            RepoMetadata.TryInitialize(new MockTracer(), this.fileSystem, DotGVFSPath, out error)
                .ShouldEqual(true);
        }

        [TearDown]
        public void TearDown()
        {
            RepoMetadata.Shutdown();
        }

        [TestCase]
        public void MissingDiskLayoutVersionReturnsSpecificReturnCode()
        {
            int majorVersion;
            int minorVersion;
            string error;
            ReturnCode returnCode;

            RepoMetadata.Instance.TryGetOnDiskLayoutVersion(out majorVersion, out minorVersion, out error, out returnCode)
                .ShouldEqual(false);

            returnCode.ShouldEqual(ReturnCode.MissingDiskLayoutVersion);
            error.ShouldEqual(RepoMetadata.MissingDiskLayoutVersionMessage);
        }

        [TestCase]
        public void InvalidDiskLayoutVersionReturnsGenericError()
        {
            string metadataPath = RepoMetadata.Instance.DataFilePath;
            KeyValuePair<string, string> diskLayoutVersion = new KeyValuePair<string, string>(DiskLayoutMajorVersionKey, "not-an-int");
            this.fileSystem.WriteAllText(metadataPath, "A " + GVFSJsonOptions.Serialize(diskLayoutVersion).Trim() + "\r\n");

            RepoMetadata.Shutdown();

            string initializeError;
            RepoMetadata.TryInitialize(new MockTracer(), this.fileSystem, DotGVFSPath, out initializeError)
                .ShouldEqual(true);

            int majorVersion;
            int minorVersion;
            string error;
            ReturnCode returnCode;

            RepoMetadata.Instance.TryGetOnDiskLayoutVersion(out majorVersion, out minorVersion, out error, out returnCode)
                .ShouldEqual(false);

            returnCode.ShouldEqual(ReturnCode.GenericError);
            error.ShouldContain("Failed to parse persisted disk layout version number");
        }
    }
}
