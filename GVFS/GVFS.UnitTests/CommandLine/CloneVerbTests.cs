using GVFS.CommandLine;
using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace GVFS.UnitTests.CommandLine
{
    [TestFixture]
    public class CloneVerbTests
    {
        private const string TestRoot = @"mock:\clone";
        private const string PackRoot = @"mock:\cache\pack";
        private Mock<PhysicalFileSystem> fileSystem;
        private List<string> packFiles;
        private HashSet<string> files;

        [SetUp]
        public void SetUp()
        {
            this.packFiles = new List<string>();
            this.files = new HashSet<string>();
            this.fileSystem = new Mock<PhysicalFileSystem>();
            this.fileSystem.Setup(fs => fs.DirectoryExists(PackRoot)).Returns(true);
            this.fileSystem.Setup(fs => fs.GetFiles(PackRoot, "prefetch-*.pack")).Returns(() => this.packFiles.ToArray());
            this.fileSystem.Setup(fs => fs.FileExists(It.IsAny<string>())).Returns((string path) => this.files.Contains(path));
        }

        [TestCase("prefetch-1000-aabb.pack", true, false, true)]
        [TestCase("prefetch-1000-aabb.pack", false, false, false)]
        [TestCase("prefetch-1000-aabb.pack", true, true, false)]
        [TestCase("prefetch-invalid-aabb.pack", true, false, false)]
        public void BackgroundPrefetchUsesCompletedCachedPacks(
            string packName,
            bool hasIndex,
            bool incomplete,
            bool expectCachedPrefetch)
        {
            string packPath = Path.Combine(PackRoot, packName);
            this.packFiles.Add(packPath);
            if (hasIndex)
            {
                this.files.Add(Path.ChangeExtension(packPath, ".idx"));
            }

            if (incomplete)
            {
                this.files.Add(Path.ChangeExtension(packPath, GVFSConstants.InProgressPrefetchMarkerExtension));
            }

            this.AssertBackgroundPrefetch(expectCachedPrefetch);
        }

        [Test]
        public void BackgroundPrefetchUsesAnyCompletedPackAlongsideAnIncompletePack()
        {
            string completePack = Path.Combine(PackRoot, "prefetch-1000-aabb.pack");
            string incompletePack = Path.Combine(PackRoot, "prefetch-2000-ccdd.pack");
            this.packFiles.Add(incompletePack);
            this.packFiles.Add(completePack);
            this.files.Add(Path.ChangeExtension(completePack, ".idx"));
            this.files.Add(Path.ChangeExtension(incompletePack, ".idx"));
            this.files.Add(Path.ChangeExtension(incompletePack, GVFSConstants.InProgressPrefetchMarkerExtension));

            this.AssertBackgroundPrefetch(expectCachedPrefetch: true);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BackgroundPrefetchAnnouncesInitialPrefetchWithoutCachedPacks(bool directoryExists)
        {
            this.fileSystem.Setup(fs => fs.DirectoryExists(PackRoot)).Returns(directoryExists);
            this.AssertBackgroundPrefetch(expectCachedPrefetch: false);
        }

        [TestCase(false)]
        [TestCase(true)]
        [NUnit.Framework.Category(CategoryConstants.ExceptionExpected)]
        public void CacheInspectionFailureLogsWarningAndKeepsInitialPrefetch(bool accessDenied)
        {
            using (MockTracer tracer = new MockTracer())
            {
                Exception failure = accessDenied
                    ? new UnauthorizedAccessException("Cache access denied")
                    : new IOException("Cache read failed");
                this.fileSystem.Setup(fs => fs.GetFiles(PackRoot, "prefetch-*.pack")).Throws(failure);

                Assert.That(CloneVerb.HasPrefetchPacks(tracer, this.fileSystem.Object, PackRoot), Is.False);
                Assert.That(tracer.RelatedWarningEvents, Has.Count.EqualTo(1));
                Assert.That(tracer.RelatedWarningEvents[0], Does.Contain(failure.Message));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        [NUnit.Framework.Category(CategoryConstants.ExceptionExpected)]
        public void BackgroundPrefetchLaunchFailureRemainsVisible(bool hasPrefetchPacks)
        {
            using (StringWriter output = new StringWriter())
            {
                CloneVerb verb = new CloneVerb { Output = output };
                verb.StartBackgroundPrefetch(
                    TestRoot,
                    hasPrefetchPacks,
                    _ => throw new Win32Exception("Prefetch launch failed"));

                Assert.That(output.ToString(), Does.Contain("Error starting prefetch: Prefetch launch failed"));
                Assert.That(output.ToString(), Does.Contain("Run 'gvfs prefetch --commits'"));
                Assert.That(output.ToString(), Does.Not.Contain("has been started"));
            }
        }

        private void AssertBackgroundPrefetch(bool expectCachedPrefetch)
        {
            using (MockTracer tracer = new MockTracer())
            using (StringWriter output = new StringWriter())
            {
                bool hasPrefetchPacks = CloneVerb.HasPrefetchPacks(tracer, this.fileSystem.Object, PackRoot);
                Assert.That(hasPrefetchPacks, Is.EqualTo(expectCachedPrefetch));

                CloneVerb verb = new CloneVerb { Output = output };
                int launches = 0;
                verb.StartBackgroundPrefetch(
                    TestRoot,
                    hasPrefetchPacks,
                    startInfo =>
                    {
                        ++launches;
                        Assert.That(startInfo.FileName, Is.EqualTo(Environment.ProcessPath));
                        Assert.That(startInfo.Arguments, Is.EqualTo("prefetch --commits"));
                        Assert.That(startInfo.WorkingDirectory, Is.EqualTo(TestRoot));
                        Assert.That(startInfo.UseShellExecute, Is.True);
                        Assert.That(
                            startInfo.WindowStyle,
                            Is.EqualTo(expectCachedPrefetch ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Minimized));
                    });

                Assert.That(launches, Is.EqualTo(1));
                if (expectCachedPrefetch)
                {
                    Assert.That(output.ToString(), Is.Empty);
                }
                else
                {
                    Assert.That(output.ToString(), Does.Contain("Prefetch of commit graph has been started as a background process."));
                }
            }
        }
    }
}
