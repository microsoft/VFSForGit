using GVFS.Common.NamedPipes;
using System;

namespace GVFS.UnitTests.Mock.Common
{
    public class MockNamedPipeClient : NamedPipeClient
    {
        public MockNamedPipeClient() : base("mock-pipe")
        {
        }

        public Exception ConnectError { get; set; }
        public Exception SendError { get; set; }
        public Exception ReadError { get; set; }
        public Func<NamedPipeMessages.Message> ReadResponseCallback { get; set; }
        public NamedPipeMessages.Message Response { get; set; }
        public bool Disposed { get; private set; }
        public int ConnectTimeoutMs { get; private set; }

        public override bool Connect(int timeoutMilliseconds, out Exception error)
        {
            this.ConnectTimeoutMs = timeoutMilliseconds;
            error = this.ConnectError;
            return error == null;
        }

        public override void SendRequest(NamedPipeMessages.Message message)
        {
            if (this.SendError != null)
            {
                throw this.SendError;
            }
        }

        public override NamedPipeMessages.Message ReadResponse()
        {
            if (this.ReadError != null)
            {
                throw this.ReadError;
            }

            return this.ReadResponseCallback == null ? this.Response : this.ReadResponseCallback();
        }

        public override void Dispose()
        {
            this.Disposed = true;
        }
    }
}
