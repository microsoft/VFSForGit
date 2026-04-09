using System;
using System.IO;

namespace GVFS.Common.Git
{
    /// <summary>
    /// As you read from a SideChannelStream, we read from the inner
    /// 'from' stream and write that data to the inner 'to' stream
    /// before passing the bytes out to the reader.
    /// </summary>
    public class SideChannelStream : Stream
    {
        protected readonly Stream from;
        protected readonly Stream to;

        public SideChannelStream(Stream from, Stream to)
        {
            this.from = from;
            this.to = to;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => 0;

        public override long Position { get => 0; set => throw new NotImplementedException(); }

        public override void Flush()
        {
            this.from.Flush();
            this.to.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = this.from.Read(buffer, offset, count);
            this.to.Write(buffer, offset, n);
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }
    }
}
