using GVFS.Common.Git;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using NUnit.Framework;

namespace GVFS.UnitTests.Common.Git
{
    [TestFixture]
    public class Sha1IdTests
    {
        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void TryParseFailsForLowerCaseShas()
        {
            Sha1Id sha1;
            string error;
            Sha1Id.TryParse("abcdef7890123456789012345678901234567890", out sha1, out error).ShouldBeFalse();
            Sha1Id.TryParse(new string('a', 40), out sha1, out error).ShouldBeFalse();
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void TryParseFailsForInvalidShas()
        {
            Sha1Id sha1;
            string error;
            Sha1Id.TryParse(null, out sha1, out error).ShouldBeFalse();
            Sha1Id.TryParse("0", out sha1, out error).ShouldBeFalse();
            Sha1Id.TryParse("abcdef", out sha1, out error).ShouldBeFalse();
            Sha1Id.TryParse(new string('H', 40), out sha1, out error).ShouldBeFalse();
        }

        [TestCase]
        public void TryParseSucceedsForUpperCaseShas()
        {
            Sha1Id sha1Id;
            string error;
            string sha = "ABCDEF7890123456789012345678901234567890";
            Sha1Id.TryParse(sha, out sha1Id, out error).ShouldBeTrue();
            sha1Id.ToString().ShouldEqual(sha);

            sha = new string('A', 40);
            Sha1Id.TryParse(sha, out sha1Id, out error).ShouldBeTrue();
            sha1Id.ToString().ShouldEqual(sha);
        }
    }
}
