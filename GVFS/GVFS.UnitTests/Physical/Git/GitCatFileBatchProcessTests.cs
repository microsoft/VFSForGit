using GVFS.Common.Git;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace GVFS.UnitTests.Physical.Git
{
    [TestFixture]
    public class GitCatFileBatchProcessTests
    {
        private const string TestTreeCommitId = "HEAD";
        private const string TestTreeSha = "2e866d08e55a796755c1889ae5b30d0e8fad5572";
        private static readonly Encoding GitOutputEncoding = Encoding.GetEncoding(1252);
        private Random randy = new Random(0);

        [TestCase]
        public void ProcessesNamedEntry()
        {
            GitTreeEntry[] inputData = new[]
            {
                new GitTreeEntry("file", this.RandomSha(), false, true),
                new GitTreeEntry("Dir", this.RandomSha(), false, true)
            };

            using (MemoryStream testData = new MemoryStream())
            {
                // Create test data
                WriteTestTreeEntries(inputData, testData);

                testData.Position = 0;

                using (MemoryStream mockStdInStream = new MemoryStream())
                using (StreamWriter mockStdIn = new StreamWriter(mockStdInStream))
                {
                    GitCatFileBatchProcess dut = new GitCatFileBatchProcess(new StreamReader(testData, GitOutputEncoding), mockStdIn);
                    GitTreeEntry[] output = dut.GetTreeEntries_CanTimeout(TestTreeSha).ToArray();

                    output.Length.ShouldEqual(inputData.Length);
                    for (int i = 0; i < output.Length; ++i)
                    {
                        output[i].Sha.ShouldEqual(inputData[i].Sha);
                        output[i].Name.ShouldEqual(inputData[i].Name);
                        output[i].IsBlob.ShouldEqual(inputData[i].IsBlob);
                        output[i].IsTree.ShouldEqual(inputData[i].IsTree);
                    }
                }
            }
        }

        private static void WriteTestTreeEntries(GitTreeEntry[] inputData, MemoryStream testData)
        {
            StreamWriter writer = new StreamWriter(testData);
            writer.AutoFlush = true;

            using (MemoryStream rawTree = new MemoryStream())
            using (StreamWriter rawTreeWriter = new StreamWriter(rawTree, GitOutputEncoding))
            {
                rawTreeWriter.AutoFlush = true;
                foreach (GitTreeEntry entry in inputData)
                {
                    // Tree entry format is '<objmode> <filename>\0<20 byte sha>'
                    // End of stream is \n
                    rawTreeWriter.Write(entry.IsBlob ? "100644 " : "40000 ");
                    rawTreeWriter.Write(entry.Name + "\0");

                    byte[] bytes = StringToByteArray(entry.Sha);
                    rawTree.Write(bytes, 0, bytes.Length);
                }

                rawTree.Position = 0;

                // cat-file --batch header is '<Text Sha> <Text ObjectType> <# Bytes to follow>\n'
                writer.Write(TestTreeSha + " tree " + rawTree.Length + '\n');
                rawTree.CopyTo(testData);

                // Terminate stream with \n
                writer.Write('\n');
            }
        }

        private static byte[] StringToByteArray(string hex)
        {
            return Enumerable.Range(0, hex.Length)
                             .Where(x => x % 2 == 0)
                             .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                             .ToArray();
        }

        private string RandomSha()
        {
            byte[] bytes = new byte[20];
            this.randy.NextBytes(bytes);
            return BitConverter.ToString(bytes).Replace("-", string.Empty);
        }
    }
}
