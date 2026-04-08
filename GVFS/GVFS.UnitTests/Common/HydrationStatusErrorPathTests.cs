using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.FileSystem;
using GVFS.UnitTests.Mock.Git;
using NUnit.Framework;
using System.IO;
using System.Linq;
using System.Threading;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class HydrationStatusErrorPathTests
    {
        private const string HeadTreeId = "0123456789012345678901234567890123456789";
        private const int HeadPathCount = 42;

        private MockFileSystem fileSystem;
        private MockGitProcess gitProcess;
        private MockTracer tracer;
        private GVFSContext context;
        private string gitParentPath;
        private string gvfsMetadataPath;
        private MockDirectory enlistmentDirectory;

        [SetUp]
        public void Setup()
        {
            this.tracer = new MockTracer();

            string enlistmentRoot = Path.Combine("mock:", "GVFS", "UnitTests", "Repo");
            string statusCachePath = Path.Combine("mock:", "GVFS", "UnitTests", "Repo", GVFSPlatform.Instance.Constants.DotGVFSRoot, "gitStatusCache");

            this.gitProcess = new MockGitProcess();
            this.gitProcess.SetExpectedCommandResult($"--no-optional-locks status \"--serialize={statusCachePath}", () => new GitProcess.Result(string.Empty, string.Empty, 0), true);
            MockGVFSEnlistment enlistment = new MockGVFSEnlistment(enlistmentRoot, "fake://repoUrl", "fake://gitBinPath", this.gitProcess);
            enlistment.InitializeCachePathsFromKey("fake:\\gvfsSharedCache", "fakeCacheKey");

            this.gitParentPath = enlistment.WorkingDirectoryBackingRoot;
            this.gvfsMetadataPath = enlistment.DotGVFSRoot;

            this.enlistmentDirectory = new MockDirectory(
                enlistmentRoot,
                new MockDirectory[]
                {
                    new MockDirectory(this.gitParentPath, folders: null, files: null),
                },
                null);

            this.enlistmentDirectory.CreateFile(Path.Combine(this.gitParentPath, ".git", "config"), ".git config Contents", createDirectories: true);
            this.enlistmentDirectory.CreateFile(Path.Combine(this.gitParentPath, ".git", "HEAD"), ".git HEAD Contents", createDirectories: true);
            this.enlistmentDirectory.CreateFile(Path.Combine(this.gitParentPath, ".git", "logs", "HEAD"), "HEAD Contents", createDirectories: true);
            this.enlistmentDirectory.CreateFile(Path.Combine(this.gitParentPath, ".git", "info", "always_exclude"), "always_exclude Contents", createDirectories: true);
            this.enlistmentDirectory.CreateDirectory(Path.Combine(this.gitParentPath, ".git", "objects", "pack"));

            this.fileSystem = new MockFileSystem(this.enlistmentDirectory);
            this.fileSystem.AllowMoveFile = true;
            this.fileSystem.DeleteNonExistentFileThrowsException = false;

            this.context = new GVFSContext(
                this.tracer,
                this.fileSystem,
                new MockGitRepo(this.tracer, enlistment, this.fileSystem),
                enlistment);
        }

        [TearDown]
        public void TearDown()
        {
            this.fileSystem = null;
            this.gitProcess = null;
            this.tracer = null;
            this.context = null;
        }

        #region HydrationStatus.Response TryParse error paths

        [TestCase(null)]
        [TestCase("")]
        public void TryParse_NullOrEmpty_ReturnsFalse(string body)
        {
            bool result = NamedPipeMessages.HydrationStatus.Response.TryParse(body, out NamedPipeMessages.HydrationStatus.Response response);
            Assert.IsFalse(result);
            Assert.IsNull(response);
        }

        [TestCase("1,2,3")]
        [TestCase("1,2,3,4,5")]
        public void TryParse_TooFewParts_ReturnsFalse(string body)
        {
            bool result = NamedPipeMessages.HydrationStatus.Response.TryParse(body, out NamedPipeMessages.HydrationStatus.Response response);
            Assert.IsFalse(result);
            Assert.IsNull(response);
        }

        [TestCase("abc,2,3,4,5,6")]
        [TestCase("1,2,three,4,5,6")]
        [TestCase("1,2,3,4,5,six")]
        public void TryParse_NonIntegerValues_ReturnsFalse(string body)
        {
            bool result = NamedPipeMessages.HydrationStatus.Response.TryParse(body, out NamedPipeMessages.HydrationStatus.Response response);
            Assert.IsFalse(result);
            Assert.IsNull(response);
        }

        [TestCase("-1,0,0,0,10,5")]
        [TestCase("0,-1,0,0,10,5")]
        [TestCase("0,0,-1,0,10,5")]
        [TestCase("0,0,0,-1,10,5")]
        public void TryParse_NegativeCounts_ReturnsFalse(string body)
        {
            bool result = NamedPipeMessages.HydrationStatus.Response.TryParse(body, out NamedPipeMessages.HydrationStatus.Response response);
            Assert.IsFalse(result);
        }

        [TestCase("100,0,100,0,50,5")]
        [TestCase("0,100,0,100,10,5")]
        public void TryParse_HydratedExceedsTotal_ReturnsFalse(string body)
        {
            bool result = NamedPipeMessages.HydrationStatus.Response.TryParse(body, out NamedPipeMessages.HydrationStatus.Response response);
            Assert.IsFalse(result);
        }

        [TestCase]
        public void TryParse_ValidResponse_Succeeds()
        {
            bool result = NamedPipeMessages.HydrationStatus.Response.TryParse(
                "10,5,3,2,100,50",
                out NamedPipeMessages.HydrationStatus.Response response);

            Assert.IsTrue(result);
            Assert.AreEqual(10, response.PlaceholderFileCount);
            Assert.AreEqual(5, response.PlaceholderFolderCount);
            Assert.AreEqual(3, response.ModifiedFileCount);
            Assert.AreEqual(2, response.ModifiedFolderCount);
            Assert.AreEqual(100, response.TotalFileCount);
            Assert.AreEqual(50, response.TotalFolderCount);
            Assert.AreEqual(13, response.HydratedFileCount);
            Assert.AreEqual(7, response.HydratedFolderCount);
        }

        [TestCase]
        public void TryParse_ExtraFields_IgnoredAndSucceeds()
        {
            bool result = NamedPipeMessages.HydrationStatus.Response.TryParse(
                "10,5,3,2,100,50,extra,fields",
                out NamedPipeMessages.HydrationStatus.Response response);

            Assert.IsTrue(result);
            Assert.AreEqual(10, response.PlaceholderFileCount);
            Assert.AreEqual(100, response.TotalFileCount);
        }

        [TestCase]
        public void TryParse_ZeroCounts_IsValid()
        {
            bool result = NamedPipeMessages.HydrationStatus.Response.TryParse(
                "0,0,0,0,0,0",
                out NamedPipeMessages.HydrationStatus.Response response);

            Assert.IsTrue(result);
            Assert.IsTrue(response.IsValid);
        }

        [TestCase]
        public void ToBody_RoundTrips_WithTryParse()
        {
            NamedPipeMessages.HydrationStatus.Response original = new NamedPipeMessages.HydrationStatus.Response
            {
                PlaceholderFileCount = 42,
                PlaceholderFolderCount = 10,
                ModifiedFileCount = 8,
                ModifiedFolderCount = 3,
                TotalFileCount = 1000,
                TotalFolderCount = 200,
            };

            string body = original.ToBody();
            bool result = NamedPipeMessages.HydrationStatus.Response.TryParse(body, out NamedPipeMessages.HydrationStatus.Response parsed);

            Assert.IsTrue(result);
            Assert.AreEqual(original.PlaceholderFileCount, parsed.PlaceholderFileCount);
            Assert.AreEqual(original.PlaceholderFolderCount, parsed.PlaceholderFolderCount);
            Assert.AreEqual(original.ModifiedFileCount, parsed.ModifiedFileCount);
            Assert.AreEqual(original.ModifiedFolderCount, parsed.ModifiedFolderCount);
            Assert.AreEqual(original.TotalFileCount, parsed.TotalFileCount);
            Assert.AreEqual(original.TotalFolderCount, parsed.TotalFolderCount);
        }

        [TestCase]
        public void ToDisplayMessage_InvalidResponse_ReturnsNull()
        {
            NamedPipeMessages.HydrationStatus.Response response = new NamedPipeMessages.HydrationStatus.Response
            {
                PlaceholderFileCount = -1,
                TotalFileCount = 100,
            };

            Assert.IsNull(response.ToDisplayMessage());
        }

        [TestCase]
        public void ToDisplayMessage_ValidResponse_FormatsCorrectly()
        {
            NamedPipeMessages.HydrationStatus.Response response = new NamedPipeMessages.HydrationStatus.Response
            {
                PlaceholderFileCount = 40,
                PlaceholderFolderCount = 10,
                ModifiedFileCount = 10,
                ModifiedFolderCount = 5,
                TotalFileCount = 100,
                TotalFolderCount = 50,
            };

            string message = response.ToDisplayMessage();
            Assert.IsNotNull(message);
            Assert.That(message, Does.Contain("50%"));
            Assert.That(message, Does.Contain("30%"));
        }

        #endregion
    }
}
