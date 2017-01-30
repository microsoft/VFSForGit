using GVFS.Common.Git;
using GVFS.Tests.Should;
using NUnit.Framework;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class GitVersionTests
    {
        [TestCase]
        public void Version_Data_Null_Returns_False()
        {
            GitVersion version;
            bool success = GitVersion.TryParse(null, out version);
            success.ShouldEqual(false);
        }

        [TestCase]
        public void Version_Data_Empty_Returns_False()
        {
            GitVersion version;
            bool success = GitVersion.TryParse(string.Empty, out version);
            success.ShouldEqual(false);
        }

        [TestCase]
        public void Version_Data_Not_Enough_Numbers_Returns_False()
        {
            GitVersion version;
            bool success = GitVersion.TryParse("2.0.1.test", out version);
            success.ShouldEqual(false);
        }

        [TestCase]
        public void Version_Data_Too_Many_Numbers_Returns_True()
        {
            GitVersion version;
            bool success = GitVersion.TryParse("2.0.1.test.1.4.3.6", out version);
            success.ShouldEqual(true);
        }

        [TestCase]
        public void Version_Data_Valid_Returns_True()
        {
            GitVersion version;
            bool success = GitVersion.TryParse("2.0.1.test.1.2", out version);
            success.ShouldEqual(true);
        }

        [TestCase]
        public void Compare_Different_Platforms_Returns_False()
        {
            GitVersion version1 = new GitVersion(1, 2, 3, "test", 4, 1);
            GitVersion version2 = new GitVersion(1, 2, 3, "test1", 4, 1);

            version1.IsLessThan(version2).ShouldEqual(false);
        }

        [TestCase]
        public void Compare_Version_Equal()
        {
            GitVersion version1 = new GitVersion(1, 2, 3, "test", 4, 1);
            GitVersion version2 = new GitVersion(1, 2, 3, "test", 4, 1);

            version1.IsLessThan(version2).ShouldEqual(false);
        }

        [TestCase]
        public void Compare_Version_Major_Less()
        {
            GitVersion version1 = new GitVersion(0, 2, 3, "test", 4, 1);
            GitVersion version2 = new GitVersion(1, 2, 3, "test", 4, 1);

            version1.IsLessThan(version2).ShouldEqual(true);
        }

        [TestCase]
        public void Compare_Version_Major_Greater()
        {
            GitVersion version1 = new GitVersion(2, 2, 3, "test", 4, 1);
            GitVersion version2 = new GitVersion(1, 2, 3, "test", 4, 1);

            version1.IsLessThan(version2).ShouldEqual(false);
        }

        [TestCase]
        public void Compare_Version_Minor_Less()
        {
            GitVersion version1 = new GitVersion(1, 1, 3, "test", 4, 1);
            GitVersion version2 = new GitVersion(1, 2, 3, "test", 4, 1);

            version1.IsLessThan(version2).ShouldEqual(true);
        }

        [TestCase]
        public void Compare_Version_Minor_Greater()
        {
            GitVersion version1 = new GitVersion(1, 3, 3, "test", 4, 1);
            GitVersion version2 = new GitVersion(1, 2, 3, "test", 4, 1);

            version1.IsLessThan(version2).ShouldEqual(false);
        }

        [TestCase]
        public void Compare_Version_Build_Less()
        {
            GitVersion version1 = new GitVersion(1, 2, 2, "test", 4, 1);
            GitVersion version2 = new GitVersion(1, 2, 3, "test", 4, 1);

            version1.IsLessThan(version2).ShouldEqual(true);
        }

        [TestCase]
        public void Compare_Version_Build_Greater()
        {
            GitVersion version1 = new GitVersion(1, 2, 4, "test", 4, 1);
            GitVersion version2 = new GitVersion(1, 2, 3, "test", 4, 1);

            version1.IsLessThan(version2).ShouldEqual(false);
        }

        [TestCase]
        public void Compare_Version_Revision_Less()
        {
            GitVersion version1 = new GitVersion(1, 2, 3, "test", 3, 1);
            GitVersion version2 = new GitVersion(1, 2, 3, "test", 4, 1);

            version1.IsLessThan(version2).ShouldEqual(true);
        }

        [TestCase]
        public void Compare_Version_Revision_Greater()
        {
            GitVersion version1 = new GitVersion(1, 2, 3, "test", 5, 1);
            GitVersion version2 = new GitVersion(1, 2, 3, "test", 4, 1);

            version1.IsLessThan(version2).ShouldEqual(false);
        }

        [TestCase]
        public void Compare_Version_MinorRevision_Less()
        {
            GitVersion version1 = new GitVersion(1, 2, 3, "test", 4, 1);
            GitVersion version2 = new GitVersion(1, 2, 3, "test", 4, 2);

            version1.IsLessThan(version2).ShouldEqual(true);
        }

        [TestCase]
        public void Compare_Version_MinorRevision_Greater()
        {
            GitVersion version1 = new GitVersion(1, 2, 3, "test", 4, 2);
            GitVersion version2 = new GitVersion(1, 2, 3, "test", 4, 1);

            version1.IsLessThan(version2).ShouldEqual(false);
        }

        [TestCase]
        public void Allow_Blank_Minor_Revision()
        {
            GitVersion version;
            GitVersion.TryParse("1.2.3.test.4", out version).ShouldEqual(true);

            version.Major.ShouldEqual(1);
            version.Minor.ShouldEqual(2);
            version.Build.ShouldEqual(3);
            version.Revision.ShouldEqual(4);
            version.Platform.ShouldEqual("test");
            version.MinorRevision.ShouldEqual(0);
        }

        [TestCase]
        public void Allow_Invalid_Minor_Revision()
        {
            GitVersion version;
            GitVersion.TryParse("1.2.3.test.4.notint", out version).ShouldEqual(true);

            version.Major.ShouldEqual(1);
            version.Minor.ShouldEqual(2);
            version.Build.ShouldEqual(3);
            version.Revision.ShouldEqual(4);
            version.Platform.ShouldEqual("test");
            version.MinorRevision.ShouldEqual(0);
        }
    }
}
