using GVFS.Common.Tracing;
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

        private bool isStopping;
        private string pipeName;
        private Action<Connection> handleConnection;
        private ITracer tracer;

        private NamedPipeServerStream listeningPipe;

        private NamedPipeServer(string pipeName, ITracer tracer, Action<Connection> handleConnection)
        {
            this.pipeName = pipeName;
            this.tracer = tracer;
            this.handleConnection = handleConnection;
            this.isStopping = false;
        }

        public static NamedPipeServer StartNewServer(string pipeName, ITracer tracer, Action<ITracer, string, Connection> handleRequest)
        {
            if (pipeName.Length > MaxPipeNameLength)
            {
                throw new PipeNameLengthException(string.Format("The pipe name ({0}) exceeds the max length allowed({1})", pipeName, MaxPipeNameLength));
            }

            NamedPipeServer pipeServer = new NamedPipeServer(pipeName, tracer, connection => HandleConnection(tracer, connection, handleRequest));
            pipeServer.OpenListeningPipe();

            return pipeServer;
        }

        public void Dispose()
        {
            this.isStopping = true;

            NamedPipeServerStream pipe = Interlocked.Exchange(ref this.listeningPipe, null);
            if (pipe != null)
            {
                pipe.Dispose();
            }
        }

        private static void HandleConnection(ITracer tracer, Connection connection, Action<ITracer, string, Connection> handleRequest)
        {
            while (connection.IsConnected)
            {
                string request = connection.ReadRequest();

                if (request == null ||
                    !connection.IsConnected)
                {
                    break;
                }

                handleRequest(tracer, request, connection);
            }
        }

        private void OpenListeningPipe()
        {
            try
            {
                if (this.listeningPipe != null)
                {
                    throw new InvalidOperationException("There is already a pipe listening for a connection");
                }

                PipeSecurity security = new PipeSecurity();
                security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
                security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.CreatorOwnerSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
                security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));

                NamedPipeServerStream pipe = new NamedPipeServerStream(
                    this.pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.WriteThrough | PipeOptions.Asynchronous,
                    0, // default inBufferSize
                    0, // default outBufferSize
                    security,
                    HandleInheritability.None);

                this.listeningPipe = pipe;
                pipe.BeginWaitForConnection(this.OnNewConnection, pipe);
            }
            catch (Exception e)
            {
                this.LogErrorAndExit("OpenListeningPipe caught unhandled exception, exiting process", e);
            }
        }

        private void OnNewConnection(IAsyncResult ar)
        {
            this.listeningPipe = null;
            bool connectionBroken = false;

            NamedPipeServerStream pipe = (NamedPipeServerStream)ar.AsyncState;
            try
            {
                try
                {
                    pipe.EndWaitForConnection(ar);
                }
                catch (IOException e)
                {
                    connectionBroken = true;

                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Area", "NamedPipeServer");
                    metadata.Add("Exception", e.ToString());
                    metadata.Add(TracingConstants.MessageKey.WarningMessage, "OnNewConnection: Connection broken");
                    this.tracer.RelatedEvent(Microsoft.Diagnostics.Tracing.EventLevel.Warning, "OnNewConnectionn_EndWaitForConnection_IOException", metadata);
                }
                catch (ObjectDisposedException)
                {
                    if (!this.isStopping)
                    {
                        throw;
                    }
                }
                catch (Exception e)
                {
                    this.LogErrorAndExit("OnNewConnection caught unhandled exception, exiting process", e);
                }

                if (!this.isStopping)
                {
                    this.OpenListeningPipe();

                    if (!connectionBroken)
                    {
                        try
                        {
                            this.handleConnection(new Connection(pipe, () => this.isStopping));
                        }
                        catch (Exception e)
                        {
                            this.LogErrorAndExit("Unhandled exception in connection handler", e);
                        }
                    }
                }
            }
            finally
            {
                pipe.Dispose();
            }
        }

        private void LogErrorAndExit(string message, Exception e)
        {
            if (this.tracer != null)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Area", "NamedPipeServer");
                if (e != null)
                {
                    metadata.Add("Exception", e.ToString());
                }

                this.tracer.RelatedError(metadata, message);
            }

            Environment.Exit((int)ReturnCode.GenericError);
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

            public NamedPipeMessages.Message ReadMessage()
            {
                return NamedPipeMessages.Message.FromString(this.ReadRequest());
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
