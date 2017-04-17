using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;

namespace GVFS.Common.NamedPipes
{
    public class NamedPipeServer : IDisposable
    {
        // Tests show that 250 is the max supported pipe name length
        private const int MaxPipeNameLength = 250;

        private ManualResetEventSlim signalConnected;
        private bool isStopping;
        private string pipeName;
        private Action<Connection> handleConnection;

        private NamedPipeServer(string pipeName, Action<Connection> handleConnection)
        {
            this.pipeName = pipeName;
            this.handleConnection = handleConnection;
            this.signalConnected = new ManualResetEventSlim(initialState: false);
            this.isStopping = false;
        }

        public static NamedPipeServer StartNewServer(string pipeName, Action<string, Connection> handleRequest)
        {
            if (pipeName.Length > MaxPipeNameLength)
            {
                throw new PipeNameLengthException(string.Format("The pipe name ({0}) exceeds the max length allowed({1})", pipeName, MaxPipeNameLength));
            }

            NamedPipeServer pipeServer = new NamedPipeServer(pipeName, connection => HandleConnection(connection, handleRequest));
            pipeServer.Start();

            return pipeServer;
        }

        public void Start()
        {
            this.CreateNewListenerThread();
        }

        public void Dispose()
        {
            this.isStopping = true;
            this.signalConnected.Set();
            this.signalConnected.Dispose();
        }

        private static void HandleConnection(Connection connection, Action<string, Connection> handleRequest)
        {
            while (connection.IsConnected)
            {
                string request = connection.ReadRequest();

                if (request == null ||
                    !connection.IsConnected)
                {
                    break;
                }

                handleRequest(request, connection);
            }
        }

        private void CreateNewListenerThread()
        {
            // Don't create a new thread if the server has been disposed.
            if (this.isStopping)
            {
                return;
            }

            new Thread(this.ListenForNewConnection).Start();
        }

        private void ListenForNewConnection()
        {
            PipeSecurity security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.CreatorOwnerSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));

            try
            {
                using (NamedPipeServerStream serverStream = new NamedPipeServerStream(
                      this.pipeName,
                      PipeDirection.InOut,
                      NamedPipeServerStream.MaxAllowedServerInstances,
                      PipeTransmissionMode.Byte,
                      PipeOptions.WriteThrough | PipeOptions.Asynchronous,
                      0, // default inBufferSize
                      0, // default outBufferSize
                      security,
                      HandleInheritability.None))
                {
                    IAsyncResult asyncResult = serverStream.BeginWaitForConnection(ar => this.signalConnected.Set(), state: null);

                    this.signalConnected.Wait();

                    if (asyncResult.IsCompleted && !this.isStopping)
                    {
                        this.signalConnected.Reset();

                        serverStream.EndWaitForConnection(asyncResult);
                        this.CreateNewListenerThread();

                        this.handleConnection(new Connection(serverStream, () => this.isStopping));
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                // If signalConnected has been disposed, then the server is shutting down.
            }
        }

        public class Connection
        {
            private NamedPipeServerStream serverStream;
            private StreamReader reader;
            private StreamWriter writer;
            private Func<bool> isStopping;

            public Connection(NamedPipeServerStream serverStream, Func<bool> isStopping)
            {
                this.serverStream = serverStream;
                this.isStopping = isStopping;
                this.reader = new StreamReader(this.serverStream);
                this.writer = new StreamWriter(this.serverStream);
            }

            public bool IsConnected
            {
                get { return !this.isStopping() && this.serverStream.IsConnected; }
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
        }
    }
}
