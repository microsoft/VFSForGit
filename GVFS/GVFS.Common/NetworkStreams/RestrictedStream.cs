using System;
using System.IO;

namespace GVFS.Common.NetworkStreams
{
    /// <summary>
    /// Stream wrapper for a length-limited subview of another stream.
    /// </summary>
    internal class RestrictedStream : Stream
    {
        private readonly Stream stream;
        private readonly long length;
        private readonly bool leaveOpen;

        private long position;
        private bool closed;

        public RestrictedStream(Stream stream, long length, bool leaveOpen = true)
        {
            this.stream = stream;
            this.length = length;
            this.leaveOpen = leaveOpen;
        }

        public override bool CanRead
        {
            get
            {
                return true;
            }
        }

        public override bool CanSeek
        {
            get
            {
                return this.stream.CanSeek;
            }
        }

        public override bool CanWrite
        {
            get
            {
                return false;
            }
        }

        public override long Length
        {
            get
            {
                return this.length;
            }
        }

        public override long Position
        {
            get
            {
                return this.position;
            }

            set
            {
                this.Seek(value, SeekOrigin.Begin);
            }
        }

        public override void Close()
        {
            if (!this.closed)
            {
                this.closed = true;

                if (!this.leaveOpen)
                {
                    this.stream.Close();
                }
            }

            base.Close();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int bytesToRead = (int)(Math.Min(this.position + count, this.length) - this.position);

            // Some streams like HttpContent.ReadOnlyStream throw InvalidOperationException
            // when reading 0 bytes from huge streams. If that changes we can remove this check.
            if (bytesToRead == 0)
            {
                return 0;
            }

            int toReturn = this.stream.Read(buffer, offset, bytesToRead);

            this.position += toReturn;

            return toReturn;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (!this.stream.CanSeek)
            {
                throw new InvalidOperationException();
            }

            long newPosition;

            switch (origin)
            {
                case SeekOrigin.Begin:
                    newPosition = offset;
                    break;

                case SeekOrigin.Current:
                    newPosition = this.position + offset;
                    break;

                case SeekOrigin.End:
                    newPosition = this.length + offset;
                    break;

                default:
                    throw new InvalidOperationException();
            }

            newPosition = Math.Max(Math.Min(this.length, newPosition), 0);

            this.stream.Seek(newPosition - this.position, SeekOrigin.Current);

            this.position = newPosition;

            return newPosition;
        }

        public override void Flush()
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}
