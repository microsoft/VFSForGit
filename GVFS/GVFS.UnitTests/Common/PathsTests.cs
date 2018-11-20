using GVFS.Common;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.Runtime.InteropServices;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class PathsTests
    {
        // Cannot use the System.IO.Path.(Alt)DirectorySeparatorChar field because
        // this is different depending on the platform the tests are being run on.
        private const char WindowsPathSeparator = '\\';
        private const char UnixPathSeparator = '/';

        [TestCase]
        public void CanConvertOSPathToGitFormat()
        {
            string systemPath;
            string expectedGitPath;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                systemPath = @"C:\This\is\a\path";
                expectedGitPath = @"C:/This/is/a/path";
            }
            else
            {
                systemPath = @"/This/is/a/path";
                expectedGitPath = systemPath;
            }

            string actualTransformedPath = Paths.ConvertPathToGitFormat(systemPath);
            actualTransformedPath.ShouldEqual(expectedGitPath);

            string doubleTransformedPath = Paths.ConvertPathToGitFormat(actualTransformedPath);
            doubleTransformedPath.ShouldEqual(expectedGitPath);
        }

        [TestCase(@"C:\", @"C:\a\b\c\file.txt", @"a\b\c\file.txt")]
        [TestCase(@"C:\a\", @"C:\a\b\c\file.txt", @"b\c\file.txt")]
        [TestCase(@"C:\a\b\", @"C:\a\b\c\file.txt", @"c\file.txt")]
        [TestCase(@"C:\a\b\c\", @"C:\a\b\c\file.txt", @"file.txt")]
        [TestCase(@"C:\a\d\e\", @"C:\a\b\c\file.txt", @"..\..\b\c\file.txt")]
        [TestCase(@"C:\a\b\..\b\d\", @"C:\a\b\c\..\c\file.txt", @"..\c\file.txt")]
        [TestCase(@"C:\a\", @"C:\a\b\c\", @"b\c\")]
        [TestCase(@"C:\a\b\c\file.txt", @"C:\a\b\c\file.txt", @".")]
        [TestCase(@"C:\a\b\c\", @"C:\a\b\c\", @".")]
        [TestCase(@"Z:\a\b\", @"C:\a\b\c\file.txt", @"C:\a\b\c\file.txt")]
        [TestCase(@"C:\a\spacey dir\", @"C:\a\spacey dir\c\spacey file.txt", @"c\spacey file.txt")]
        public void GetRelativePath_Windows(string relativeTo, string path, string expected)
        {
            var actual = Paths.GetRelativePath(relativeTo, path);

            // If we're running on non-Windows the relative path is returned with platform-
            // native path separators, even if a Windows-style path was passed in.
            // Convert it back to the Windows-style separator "\".
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                actual = actual.Replace(UnixPathSeparator, WindowsPathSeparator);
            }

            actual.ShouldEqual(expected);
        }

        [TestCase(@"/", @"/a/b/c/file.txt", @"a/b/c/file.txt")]
        [TestCase(@"/a/", @"/a/b/c/file.txt", @"b/c/file.txt")]
        [TestCase(@"/a/b/", @"/a/b/c/file.txt", @"c/file.txt")]
        [TestCase(@"/a/b/c/", @"/a/b/c/file.txt", @"file.txt")]
        [TestCase(@"/a/d/e/", @"/a/b/c/file.txt", @"../../b/c/file.txt")]
        [TestCase(@"/a/b/../b/d/", @"/a/b/c/../c/file.txt", @"../c/file.txt")]
        [TestCase(@"/a/", @"/a/b/c/", @"b/c/")]
        [TestCase(@"/a/b/c/file.txt", @"/a/b/c/file.txt", @".")]
        [TestCase(@"/a/b/c/", @"/a/b/c/", @".")]
        [TestCase(@"/a/spacey dir/", @"/a/spacey dir/c/spacey file.txt", @"c/spacey file.txt")]
        public void GetRelativePath_Unix(string relativeTo, string path, string expected)
        {
            var actual = Paths.GetRelativePath(relativeTo, path);

            // If we're running on Windows the relative path is returned with platform-
            // native path separators, even if a UNIX-style path was passed in.
            // Convert it back to the UNIX-style separator "/".
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                actual = actual.Replace(WindowsPathSeparator, UnixPathSeparator);
            }

            actual.ShouldEqual(expected);
        }
    }
}
