using System;
using System.IO;
using System.IO.Pipes;

namespace RGFS.Common.NamedPipes
{
    public class NamedPipeClient : IDisposable
    {
        private string pipeName;
        private NamedPipeClientStream clientStream;
        private StreamReader reader;
        private StreamWriter writer;

        public NamedPipeClient(string pipeName)
        {
            this.pipeName = pipeName;
        }

        public static string GetPipeNameFromPath(string path)
        {
            return Paths.GetNamedPipeName(path);
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

            this.reader = new StreamReader(this.clientStream);
            this.writer = new StreamWriter(this.clientStream);

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
                this.writer.WriteLine(message);
                this.writer.Flush();
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
                string response = this.reader.ReadLine();
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
