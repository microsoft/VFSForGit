using GVFS.GVFlt;
using GVFS.Tests.Should;
using NUnit.Framework;

namespace GVFS.UnitTests.GVFlt
{
    [TestFixture]
    public class PathUtilTests
    {
        [TestCase]
        public void EmptyStringIsNotInsideDotGitPath()
        {
            PathUtil.IsPathInsideDotGit(string.Empty).ShouldEqual(false);
        }

        [TestCase]
        public void IsPathInsideDotGitIsTrueForDotGitPath()
        {
            PathUtil.IsPathInsideDotGit(@".git\").ShouldEqual(true);
            PathUtil.IsPathInsideDotGit(@".GIT\").ShouldEqual(true);
            PathUtil.IsPathInsideDotGit(@".git\test_file.txt").ShouldEqual(true);
            PathUtil.IsPathInsideDotGit(@".GIT\test_file.txt").ShouldEqual(true);
            PathUtil.IsPathInsideDotGit(@".git\test_folder\test_file.txt").ShouldEqual(true);
            PathUtil.IsPathInsideDotGit(@".GIT\test_folder\test_file.txt").ShouldEqual(true);
        }

        [TestCase]
        public void IsPathInsideDotGitIsFalseForNonDotGitPath()
        {
            PathUtil.IsPathInsideDotGit(@".git").ShouldEqual(false);
            PathUtil.IsPathInsideDotGit(@".GIT").ShouldEqual(false);
            PathUtil.IsPathInsideDotGit(@".gitattributes").ShouldEqual(false);
            PathUtil.IsPathInsideDotGit(@".gitignore").ShouldEqual(false);
            PathUtil.IsPathInsideDotGit(@".gitsubfolder\").ShouldEqual(false);
            PathUtil.IsPathInsideDotGit(@".gitsubfolder\test_file.txt").ShouldEqual(false);
            PathUtil.IsPathInsideDotGit(@"test_file.txt").ShouldEqual(false);
            PathUtil.IsPathInsideDotGit(@"test_folder\test_file.txt").ShouldEqual(false);
        }

        [TestCase]
        public void RemoveTrailingSlashIfPresent()
        {
            PathUtil.RemoveTrailingSlashIfPresent(string.Empty).ShouldEqual(string.Empty);
            PathUtil.RemoveTrailingSlashIfPresent(@"C:\test").ShouldEqual(@"C:\test");
            PathUtil.RemoveTrailingSlashIfPresent(@"C:\test\").ShouldEqual(@"C:\test");
            PathUtil.RemoveTrailingSlashIfPresent(@"C:\test\\").ShouldEqual(@"C:\test");
            PathUtil.RemoveTrailingSlashIfPresent(@"C:\test\\\").ShouldEqual(@"C:\test");
        }
    }
}
