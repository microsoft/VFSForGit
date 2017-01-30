using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Threading;

namespace GVFS.Common.NamedPipes
{
    public class NamedPipeServer
    {
        private string pipeName;
        private Action<Connection> handleConnection;

        public NamedPipeServer(string pipeName, Action<Connection> handleConnection)
        {
            this.pipeName = pipeName;
            this.handleConnection = handleConnection;
        }

        public void Start()
        {
            this.CreateNewListenerThread();
        }

        private void CreateNewListenerThread()
        {
            new Thread(this.ListenForNewConnection).Start();
        }

        private void ListenForNewConnection()
        {
            PipeSecurity security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule("Users", PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule("CREATOR OWNER", PipeAccessRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule("SYSTEM", PipeAccessRights.FullControl, AccessControlType.Allow));

            NamedPipeServerStream serverStream = new NamedPipeServerStream(
                this.pipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.WriteThrough,
                0, // default inBufferSize
                0, // default outBufferSize
                security,
                HandleInheritability.None);

            serverStream.WaitForConnection();

            this.CreateNewListenerThread();

            using (Connection connection = new Connection(serverStream))
            {
                this.handleConnection(connection);
            }
        }

        public class Connection : IDisposable
        {
            private NamedPipeServerStream serverStream;
            private StreamReader reader;
            private StreamWriter writer;

            public Connection(NamedPipeServerStream serverStream)
            {
                this.serverStream = serverStream;
                this.reader = new StreamReader(this.serverStream);
                this.writer = new StreamWriter(this.serverStream);
            }

            public bool IsConnected
            {
                get { return this.serverStream.IsConnected; }
            }

            public string ReadRequest()
            {
                try
                {
                    return this.reader.ReadLine();
                }
                catch (IOException)
                {
                    return null;
                }
            }

            public bool TrySendResponse(string message)
            {
                try
                {
                    this.writer.WriteLine(message);
                    this.writer.Flush();

                    return true;
                }
                catch (IOException)
                {
                    return false;
                }
            }

            public bool TrySendResponse(NamedPipeMessages.Message message)
            {
                return this.TrySendResponse(message.ToString());
            }

            public void Dispose()
            {
                this.serverStream.Dispose();
                this.serverStream = null;
                this.reader = null;
                this.writer = null;
            }
        }
    }
}
