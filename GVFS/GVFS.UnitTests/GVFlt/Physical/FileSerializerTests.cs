using GVFS.GVFlt.DotGit;
using GVFS.Tests.Should;
using GVFS.UnitTests.Virtual;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.UnitTests.GVFlt.Physical
{
    [TestFixture]
    public class FileSerializerTests : TestsWithCommonRepo
    {
        [TestCase]
        public void SerializerCreatesEmptyFileOnRead()
        {
            string filePath = Path.Combine(this.Repo.GitParentPath, "test-file");
            FileSerializer fileSerializer = new FileSerializer(this.Repo.Context, filePath);
            this.Repo.Context.FileSystem.FileExists(filePath).ShouldEqual(false);
            fileSerializer.ReadAll().ShouldBeEmpty();
            this.Repo.Context.FileSystem.FileExists(filePath).ShouldEqual(true);
        }

        [TestCase]
        public void SerializerCanAppend()
        {
            string filePath = Path.Combine(this.Repo.GitParentPath, "test-file");
            FileSerializer fileSerializer = new FileSerializer(this.Repo.Context, filePath);
            this.Repo.Context.FileSystem.FileExists(filePath).ShouldEqual(false);
            fileSerializer.ReadAll().ShouldBeEmpty();
            this.Repo.Context.FileSystem.FileExists(filePath).ShouldEqual(true);

            List<string> lines = new List<string>() { "test1", "test2", "test3" };
            foreach (string line in lines)
            {
                fileSerializer.AppendLine(line);
            }

            this.Repo.Context.FileSystem.ReadLines(filePath).Count().ShouldEqual(lines.Count);

            IEnumerator<string> expectedLines = lines.GetEnumerator();
            expectedLines.MoveNext().ShouldEqual(true);
            foreach (string fileLine in this.Repo.Context.FileSystem.ReadLines(filePath))
            {
                expectedLines.Current.ShouldEqual(fileLine);
                expectedLines.MoveNext();
            }
        }

        [TestCase]
        public void SerializerCanLoad()
        {
            string filePath = Path.Combine(this.Repo.GitParentPath, "test-file");
            FileSerializer fileSerializer = new FileSerializer(this.Repo.Context, filePath);
            this.Repo.Context.FileSystem.FileExists(filePath).ShouldEqual(false);
            fileSerializer.ReadAll().ShouldBeEmpty();
            this.Repo.Context.FileSystem.FileExists(filePath).ShouldEqual(true);

            List<string> lines = new List<string>() { "test1", "test2", "test3" };
            foreach (string line in lines)
            {
                fileSerializer.AppendLine(line);
            }

            fileSerializer.ReadAll().Count().ShouldEqual(lines.Count);
            IEnumerator<string> expectedLines = lines.GetEnumerator();
            expectedLines.MoveNext();
            foreach (string fileLine in fileSerializer.ReadAll())
            {
                expectedLines.Current.ShouldEqual(fileLine);
                expectedLines.MoveNext();
            }
        }
    }
}
