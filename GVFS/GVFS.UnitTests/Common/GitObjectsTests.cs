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
            GitObjects.IsLooseObjectsDirectory("BB").ShouldBeTrue();
            GitObjects.IsLooseObjectsDirectory("bb").ShouldBeTrue();
            GitObjects.IsLooseObjectsDirectory("A7").ShouldBeTrue();
            GitObjects.IsLooseObjectsDirectory("55").ShouldBeTrue();
            GitObjects.IsLooseObjectsDirectory("K7").ShouldBeFalse();
            GitObjects.IsLooseObjectsDirectory("A-").ShouldBeFalse();
            GitObjects.IsLooseObjectsDirectory("?B").ShouldBeFalse();
            GitObjects.IsLooseObjectsDirectory("BBB").ShouldBeFalse();
            GitObjects.IsLooseObjectsDirectory("B-B").ShouldBeFalse();
        }
    }
}
