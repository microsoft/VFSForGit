using GVFS.Common.FileSystem;
using GVFS.Tests.Should;
using System.Collections.Generic;
using System.IO;

namespace GVFS.UnitTests.Mock.FileSystem
{
    public class ConfigurableFileSystem : PhysicalFileSystem
    {
        public ConfigurableFileSystem()
        {
            this.ExpectedFiles = new Dictionary<string, ReusableMemoryStream>();
            this.ExpectedDirectories = new HashSet<string>();
        }

        public Dictionary<string, ReusableMemoryStream> ExpectedFiles { get; }
        public HashSet<string> ExpectedDirectories { get; }

        public override void CreateDirectory(string path)
        {
        }

        public override void MoveAndOverwriteFile(string sourceFileName, string destinationFilename)
        {
            ReusableMemoryStream source;
            this.ExpectedFiles.TryGetValue(sourceFileName, out source).ShouldEqual(true, "Source file does not exist: " + sourceFileName);
            this.ExpectedFiles.ContainsKey(destinationFilename).ShouldEqual(true, "MoveAndOverwriteFile expects the destination file to exist: " + destinationFilename);

            this.ExpectedFiles.Remove(sourceFileName);
            this.ExpectedFiles[destinationFilename] = source;
        }

        public override Stream OpenFileStream(string path, FileMode fileMode, FileAccess fileAccess, FileShare shareMode, FileOptions options, bool flushesToDisk)
        {
            ReusableMemoryStream stream;
            this.ExpectedFiles.TryGetValue(path, out stream).ShouldEqual(true, "Unexpected access of file: " + path);
            return stream;
        }

        public override bool FileExists(string path)
        {
            return this.ExpectedFiles.ContainsKey(path);
        }

        public override bool DirectoryExists(string path)
        {
            return this.ExpectedDirectories.Contains(path);
        }
    }
}
