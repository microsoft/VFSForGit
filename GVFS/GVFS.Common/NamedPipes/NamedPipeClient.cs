using System;
using System.IO;
using System.IO.Pipes;

namespace GVFS.Common.NamedPipes
{
    public class NamedPipeClient : IDisposable
    {
        private string pipeName;
        private NamedPipeClientStream clientStream;
        private NamedPipeStreamReader reader;
        private NamedPipeStreamWriter writer;

        public NamedPipeClient(string pipeName)
        {
            this.pipeName = pipeName;
        }

        public bool Connect(int timeoutMilliseconds = 3000)
        {
            if (this.clientStream != null)
            {
                throw new InvalidOperationException();
            }

            try
            {
                this.clientStream = new NamedPipeClientStream(this.pipeName);
                this.clientStream.Connect(timeoutMilliseconds);
            }
            catch (TimeoutException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }

            this.reader = new NamedPipeStreamReader(this.clientStream);
            this.writer = new NamedPipeStreamWriter(this.clientStream);

            return true;
        }

        public bool TrySendRequest(NamedPipeMessages.Message message)
        {
            try
            {
                this.SendRequest(message);
                return true;
            }
            catch (BrokenPipeException)
            {
            }

            return false;
        }

        public void SendRequest(NamedPipeMessages.Message message)
        {
            this.SendRequest(message.ToString());
        }

        public void SendRequest(string message)
        {
            this.ValidateConnection();

            try
            {
                this.writer.WriteMessage(message);
            }
            catch (IOException e)
            {
                throw new BrokenPipeException("Unable to send: " + message, e);
            }
        }

        public string ReadRawResponse()
        {
            try
            {
                string response = this.reader.ReadMessage();
                if (response == null)
                {
                    throw new BrokenPipeException("Unable to read from pipe", null);
                }

                return response;
            }
            catch (IOException e)
            {
                throw new BrokenPipeException("Unable to read from pipe", e);
            }
        }

        public NamedPipeMessages.Message ReadResponse()
        {
            return NamedPipeMessages.Message.FromString(this.ReadRawResponse());
        }

        public bool TryReadResponse(out NamedPipeMessages.Message message)
        {
            try
            {
                message = NamedPipeMessages.Message.FromString(this.ReadRawResponse());
                return true;
            }
            catch (BrokenPipeException)
            {
                message = null;
                return false;
            }
        }

        public void Dispose()
        {
            this.ValidateConnection();

            if (this.clientStream != null)
            {
                this.clientStream.Dispose();
                this.clientStream = null;
            }

            this.reader = null;
            this.writer = null;
        }

        private void ValidateConnection()
        {
            if (this.clientStream == null)
            {
                throw new InvalidOperationException("There is no connection");
            }
        }
    }
}
