using GVFS.Common.FileSystem;
using System;
using System.IO;

namespace GVFS.UnitTests.Mock.FileSystem
{
    public class MockFile
    {
        private ReusableMemoryStream contentStream;
        private FileProperties fileProperties;

        public MockFile(string fullName, string contents)
        {
            this.FullName = fullName;
            this.Name = Path.GetFileName(this.FullName);

            this.FileProperties = FileProperties.DefaultFile;

            this.contentStream = new ReusableMemoryStream(contents);
        }

        public MockFile(string fullName, byte[] contents)
        {
            this.FullName = fullName;
            this.Name = Path.GetFileName(this.FullName);

            this.FileProperties = FileProperties.DefaultFile;

            this.contentStream = new ReusableMemoryStream(contents);
        }

        public event Action Changed;

        public string FullName { get; set; }
        public string Name { get; set; }
        public FileProperties FileProperties
        {
            get
            {
                // The underlying content stream is the correct/true source of the file length
                // Create a new copy of the properties to make sure the length is set correctly.
                FileProperties newProperties = new FileProperties(
                    this.fileProperties.FileAttributes,
                    this.fileProperties.CreationTimeUTC,
                    this.fileProperties.LastAccessTimeUTC,
                    this.fileProperties.LastWriteTimeUTC,
                    this.contentStream.Length);

                this.fileProperties = newProperties;
                return this.fileProperties;
            }

            set
            {
                this.fileProperties = value;
                if (this.Changed != null)
                {
                    this.Changed();
                }
            }
        }

        public Stream GetContentStream()
        {
            this.contentStream.Position = 0;
            return this.contentStream;
        }
    }
}
