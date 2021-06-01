using System;
using System.IO;
using System.Security.Cryptography;

namespace GVFS.Common.Git
{
    public class HashingStream : Stream
    {
        private readonly HashAlgorithm hash;

        private Stream stream;

        private bool closed;
        private byte[] hashResult;

        public HashingStream(Stream stream)
        {
            this.stream = stream;

            this.hash = SHA1.Create();
            this.hashResult = null;
            this.hash.Initialize();
            this.closed = false;
        }

        public override bool CanSeek
        {
            get { return false; }
        }

        public byte[] Hash
        {
            get
            {
                this.FinishHash();
                return this.hashResult;
            }
        }

        public override bool CanRead
        {
            get { return true; }
        }

        public override long Length
        {
            get { return this.stream.Length; }
        }

        public override long Position
        {
            get { return this.stream.Position; }
            set { throw new NotImplementedException(); }
        }

        public override bool CanWrite
        {
            get { return false; }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Close()
        {
            if (!this.closed)
            {
                this.FinishHash();

                this.closed = true;

                if (this.stream != null)
                {
                    this.stream.Close();
                }
            }

            base.Close();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int bytesRead = this.stream.Read(buffer, offset, count);
            if (bytesRead > 0)
            {
                this.hash.TransformBlock(buffer, offset, bytesRead, null, 0);
            }

            return bytesRead;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public override void Flush()
        {
            throw new NotImplementedException();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (this.hash != null)
                {
                    this.hash.Dispose();
                }

                if (this.stream != null)
                {
                    this.stream.Dispose();
                    this.stream = null;
                }
            }

            base.Dispose(disposing);
        }

        private void FinishHash()
        {
            if (this.hashResult == null)
            {
                this.hash.TransformFinalBlock(new byte[0], 0, 0);
                this.hashResult = this.hash.Hash;
            }
        }
    }
}