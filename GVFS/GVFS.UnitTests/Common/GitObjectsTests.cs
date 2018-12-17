using GVFS.Common.Git;
using GVFS.Tests.Should;
using NUnit.Framework;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class GitObjectsTests
    {
        [TestCase]
        public void IsLooseObjectsDirectory()
        {
            GitObjects.IsLooseObjectsDirectory("BB").ShouldEqual(true);
            GitObjects.IsLooseObjectsDirectory("bb").ShouldEqual(true);
            GitObjects.IsLooseObjectsDirectory("A7").ShouldEqual(true);
            GitObjects.IsLooseObjectsDirectory("55").ShouldEqual(true);
            GitObjects.IsLooseObjectsDirectory("K7").ShouldEqual(false);
            GitObjects.IsLooseObjectsDirectory("A-").ShouldEqual(false);
            GitObjects.IsLooseObjectsDirectory("?B").ShouldEqual(false);
            GitObjects.IsLooseObjectsDirectory("BBB").ShouldEqual(false);
            GitObjects.IsLooseObjectsDirectory("B-B").ShouldEqual(false);
        }
    }
}
