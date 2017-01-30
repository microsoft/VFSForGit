using GVFS.Common;
using GVFS.Common.Physical.FileSystem;
using GVFS.Common.Physical.Git;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace GVFS.UnitTests.Virtual.DotGit
{
    [TestFixture]
    public class GitIndexTests
    {
        private readonly List<string> filesInIndex = new List<string>()
        {
            "anothernewfile.txt",
            "test.txt",
            "test1.txt",
            "test2.txt"
        };

        [TestCase]
        public void IndexV2()
        {
            using (GitIndex index = this.LoadIndex((uint)2))
            {
                index.Open();
                index.ClearSkipWorktreeAndUpdateEntry("test.txt", DateTime.UtcNow, DateTime.UtcNow, 1).ShouldEqual(CallbackResult.Success);
                index.ClearSkipWorktreeAndUpdateEntry("test1.txt", DateTime.UtcNow, DateTime.UtcNow, 1).ShouldEqual(CallbackResult.Success);
                index.Close();
            }
        }

        [TestCase]
        public void IndexV3()
        {
            using (GitIndex index = this.LoadIndex((uint)3))
            {
                index.Open();
                index.ClearSkipWorktreeAndUpdateEntry("test.txt", DateTime.UtcNow, DateTime.UtcNow, 1).ShouldEqual(CallbackResult.Success);
                index.ClearSkipWorktreeAndUpdateEntry("test1.txt", DateTime.UtcNow, DateTime.UtcNow, 1).ShouldEqual(CallbackResult.Success);
                index.Close();
            }
        }

        [TestCase]
        public void IndexV4()
        {
            using (GitIndex index = this.LoadIndex((uint)4))
            {
                index.Open();
                index.ClearSkipWorktreeAndUpdateEntry("test.txt", DateTime.UtcNow, DateTime.UtcNow, 1).ShouldEqual(CallbackResult.Success);
                index.ClearSkipWorktreeAndUpdateEntry("test1.txt", DateTime.UtcNow, DateTime.UtcNow, 1).ShouldEqual(CallbackResult.Success);
                index.Close();
            }
        }

        private GitIndex LoadIndex(uint version)
        {
            PhysicalFileSystem fileSystem = new PhysicalFileSystem();
            string workingDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string path = Path.Combine(workingDirectory, @"Data\index_v" + version);
            path.ShouldBeAPhysicalFile(fileSystem);
            GitIndex index = new GitIndex(new MockTracer(), new MockEnlistment(), path, path + ".lock");
            index.Initialize();
            return index;
        }
    }
}
