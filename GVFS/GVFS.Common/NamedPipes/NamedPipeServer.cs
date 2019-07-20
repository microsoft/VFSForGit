using GVFS.Common.Tracing;
using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace GVFS.Common.NamedPipes
{
    /// <summary>
    /// The server side of a Named Pipe used for interprocess communication.
    ///
    /// Named Pipe protocol:
    ///    The client / server process sends a "message" (or line) of data as a
    ///    sequence of bytes terminated by a 0x3 byte (ASCII control code for
    ///    End of text). Text is encoded as UTF-8 to be sent as bytes across the wire.
    ///
    /// This format was chosen so that:
    ///   1) A reasonable range of values can be transmitted across the pipe,
    ///      including null and bytes that represent newline characters.
    ///   2) It would be easy to implement in multiple places, as we
    ///      have managed and native implementations.
    /// </summary>
    public class NamedPipeServer : IDisposable
    {
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
            if (pipeName.Length > GVFSPlatform.Instance.Constants.MaxPipePathLength)
            {
                throw new PipeNameLengthException(string.Format("The pipe name ({0}) exceeds the max length allowed({1})", pipeName, GVFSPlatform.Instance.Constants.MaxPipePathLength));
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

                this.listeningPipe = GVFSPlatform.Instance.CreatePipeByName(this.pipeName);
                this.listeningPipe.BeginWaitForConnection(this.OnNewConnection, this.listeningPipe);
            }
            catch (Exception e)
            {
                this.LogErrorAndExit("OpenListeningPipe caught unhandled exception, exiting process", e);
            }
        }

        private void OnNewConnection(IAsyncResult ar)
        {
            if (!this.isStopping)
            {
                this.OnNewConnection(ar, createNewThreadIfSynchronous: true);
            }
        }

        private void OnNewConnection(IAsyncResult ar, bool createNewThreadIfSynchronous)
        {
            if (createNewThreadIfSynchronous &&
               ar.CompletedSynchronously)
            {
                // if this callback got called synchronously, we must not do any blocking IO on this thread
                // or we will block the original caller. Moving to a new thread so that it will be safe
                // to call a blocking Read on the NamedPipeServerStream

                new Thread(() => this.OnNewConnection(ar, createNewThreadIfSynchronous: false)).Start();
                return;
            }

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
                    this.tracer.RelatedEvent(EventLevel.Warning, "OnNewConnectionn_EndWaitForConnection_IOException", metadata);
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
                            this.handleConnection(new Connection(pipe, this.tracer, () => this.isStopping));
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
            private NamedPipeStreamReader reader;
            private NamedPipeStreamWriter writer;
            private ITracer tracer;
            private Func<bool> isStopping;

            public Connection(NamedPipeServerStream serverStream, ITracer tracer, Func<bool> isStopping)
            {
                this.serverStream = serverStream;
                this.tracer = tracer;
                this.isStopping = isStopping;
                this.reader = new NamedPipeStreamReader(this.serverStream);
                this.writer = new NamedPipeStreamWriter(this.serverStream);
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
                    return this.reader.ReadMessage();
                }
                catch (IOException e)
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("ExceptionMessage", e.Message);
                    metadata.Add("StackTrace", e.StackTrace);
                    this.tracer.RelatedWarning(
                        metadata: metadata,
                        message: $"Error reading message from NamedPipe: {e.Message}",
                        keywords: Keywords.Telemetry);

                    return null;
                }
            }

            public virtual bool TrySendResponse(string message)
            {
                try
                {
                    this.writer.WriteMessage(message);
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
