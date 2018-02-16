using FastFetch;
using FastFetch.Git;
using GVFS.Common.Git;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.FileSystem;
using GVFS.UnitTests.Mock.Git;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace GVFS.UnitTests.FastFetch
{
    [TestFixture]
    public class FastFetchHelperTests
    {
        [TestCase]
        public void AppendToNewlineSeparatedFileTests()
        {
            MockFileSystem fileSystem = new MockFileSystem(new MockDirectory(@"mock:\GVFS\UnitTests\Repo", null, null));

            // Validate can write to a file that doesn't exist.
            const string TestFileName = @"mock:\GVFS\UnitTests\Repo\appendTest";
            FetchHelper.AppendToNewlineSeparatedFile(fileSystem, TestFileName, "expected content line 1");
            fileSystem.ReadAllText(TestFileName).ShouldEqual("expected content line 1\n");

            // Validate that if the file doesn't end in a newline it gets a newline added.
            fileSystem.WriteAllText(TestFileName, "existing content");
            FetchHelper.AppendToNewlineSeparatedFile(fileSystem, TestFileName, "expected line 2");
            fileSystem.ReadAllText(TestFileName).ShouldEqual("existing content\nexpected line 2\n");

            // Validate that if the file ends in a newline, we don't end up with two newlines
            fileSystem.WriteAllText(TestFileName, "existing content\n");
            FetchHelper.AppendToNewlineSeparatedFile(fileSystem, TestFileName, "expected line 2");
            fileSystem.ReadAllText(TestFileName).ShouldEqual("existing content\nexpected line 2\n");
        }
    }
}