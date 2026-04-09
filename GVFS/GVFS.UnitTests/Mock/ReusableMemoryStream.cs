using System;
using System.IO;
using System.Text;

namespace GVFS.UnitTests.Mock
{
    public class ReusableMemoryStream : Stream
    {
        private byte[] contents;
        private long length;
        private long position;

        public ReusableMemoryStream(string initialContents)
        {
            this.contents = Encoding.UTF8.GetBytes(initialContents);
            this.length = this.contents.Length;
        }

        public ReusableMemoryStream(byte[] initialContents)
        {
            this.contents = initialContents;
            this.length = initialContents.Length;
        }

        public bool TruncateWrites { get; set; }

        public override bool CanRead
        {
            get { return true; }
        }

        public override bool CanSeek
        {
            get { return true; }
        }

        public override bool CanWrite
        {
            get { return true; }
        }

        public override long Length
        {
            get { return this.length; }
        }

        public override long Position
        {
            get { return this.position; }
            set { this.position = value; }
        }

        public override void Flush()
        {
            // noop
        }

        public string ReadAsString()
        {
            return Encoding.UTF8.GetString(this.contents, 0, (int)this.length);
        }

        public string ReadAt(long position, long length)
        {
            long lastPosition = this.Position;

            this.Position = position;

            byte[] bytes = new byte[length];
            this.Read(bytes, 0, (int)length);

            this.Position = lastPosition;

            return Encoding.UTF8.GetString(bytes);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int actualCount = Math.Min((int)(this.length - this.position), count);
            Array.Copy(this.contents, this.Position, buffer, offset, actualCount);
            this.Position += actualCount;

            return actualCount;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (origin == SeekOrigin.Begin)
            {
                this.position = offset;
            }
            else if (origin == SeekOrigin.End)
            {
                this.position = this.length - offset;
            }
            else
            {
                this.position += offset;
            }

            if (this.position > this.length)
            {
                this.position = this.length - 1;
            }

            return this.position;
        }

        public override void SetLength(long value)
        {
            while (value > this.contents.Length)
            {
                if (this.contents.Length == 0)
                {
                    this.contents = new byte[1024];
                }
                else
                {
                    Array.Resize(ref this.contents, this.contents.Length * 2);
                }
            }

            this.length = value;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (this.position + count > this.contents.Length)
            {
                this.SetLength(this.position + count);
            }

            if (this.TruncateWrites)
            {
                count /= 2;
            }

            Array.Copy(buffer, offset, this.contents, this.position, count);
            this.position += count;
            if (this.position > this.length)
            {
                this.length = this.position;
            }

            if (this.TruncateWrites)
            {
                throw new IOException("Could not complete write");
            }
        }

        protected override void Dispose(bool disposing)
        {
            // This method is a noop besides resetting the position.
            // The byte[] in this class is the source of truth for the contents that this
            // stream is providing, so we can't dispose it here.
            this.position = 0;
        }
    }
}
