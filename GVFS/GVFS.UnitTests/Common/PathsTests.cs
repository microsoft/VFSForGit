using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GVFS.Common;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.Runtime.InteropServices;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class PathsTests
    {
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

        [TestCase]
        public void GetFilesRecursive()
        {
            string rootDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

            List<string> expectedFiles = new List<string>
            {
                "file1.txt",
                "file2.txt",
                "a/file1.txt",
                "a/file2.txt",
                "a/b/file1.txt",
                "a/b/file2.txt",
                "a/b/c/file1.txt",
                "a/b/c/file2.txt",
                "a/b/c/d/file1.txt",
                "a/b/c/d/file2.txt"
            };

            CreateEmptyFiles(rootDirectory, expectedFiles);

            List<string> actualFiles = Paths.GetFilesRecursive(rootDirectory).ToList();

            CollectionAssert.AreEquivalent(expectedFiles, actualFiles);
        }

        private static void CreateEmptyFiles(string rootDirectory, IEnumerable<string> files)
        {
            foreach (string file in files)
            {
                string fullFilePath = Path.Combine(rootDirectory, file);

                string parentDirectory = Path.GetDirectoryName(fullFilePath);
                Directory.CreateDirectory(parentDirectory);

                File.WriteAllText(fullFilePath, null);
            }
        }
    }
}
