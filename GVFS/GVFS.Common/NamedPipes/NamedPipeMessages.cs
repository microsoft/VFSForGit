using Newtonsoft.Json;
using System;

namespace GVFS.Common.NamedPipes
{
    public static class NamedPipeMessages
    {
        public const string UnknownRequest = "UnknownRequest";
        public const string UnknownGVFSState = "UnknownGVFSState";

        private const char MessageSeparator = '|';

        public static class GetStatus
        {
            public const string Request = "GetStatus";
            public const string Mounting = "Mounting";
            public const string Ready = "Ready";
            public const string Unmounting = "Unmounting";
            public const string MountFailed = "MountFailed";

            public class Response
            {
                public string MountStatus { get; set; }
                public string EnlistmentRoot { get; set; }
                public string RepoUrl { get; set; }
                public string ObjectsUrl { get; set; }
                public int BackgroundOperationCount { get; set; }
                public string LockStatus { get; set; }
                public int DiskLayoutVersion { get; set; }

                public static Response FromJson(string json)
                {
                    return JsonConvert.DeserializeObject<Response>(json);
                }

                public string ToJson()
                {
                    return JsonConvert.SerializeObject(this);
                }
            }
        }

        public static class Unmount
        {
            public const string Request = "Unmount";
            public const string NotMounted = "NotMounted";
            public const string Acknowledged = "Ack";
            public const string Completed = "Complete";
            public const string AlreadyUnmounting = "AlreadyUnmounting";
            public const string MountFailed = "MountFailed";
        }

        public static class DownloadObject
        {
            public const string DownloadRequest = "DLO";
            public const string SuccessResult = "S";
            public const string DownloadFailed = "F";
            public const string InvalidSHAResult = "InvalidSHA";
            public const string MountNotReadyResult = "MountNotReady";

            public class Request
            {                
                public Request(Message message)
                {
                    this.RequestSha = message.Body;
                }

                public string RequestSha { get; }

                public Message CreateMessage()
                {
                    return new Message(DownloadRequest, this.RequestSha);
                }                
            }

            public class Response
            {
                public Response(string result)
                {
                    this.Result = result;
                }

                public string Result { get; }

                public Message CreateMessage()
                {
                    return new Message(this.Result, null);
                }
            }
        }

        public static class AcquireLock
        {
            public const string AcquireRequest = "AcquireLock";
            public const string DenyGVFSResult = "LockDeniedGVFS";
            public const string DenyGitResult = "LockDeniedGit";
            public const string AcceptResult = "LockAcquired";
            public const string MountNotReadyResult = "MountNotReady";

            public class Request
            {
                public Request(int pid, string parsedCommand, string originalCommand)
                {
                    this.RequestData = new Data(pid, parsedCommand, originalCommand);
                }

                public Request(Message message)
                {
                    this.RequestData = message.DeserializeBody<Data>();
                }

                public Data RequestData { get; }

                public Message CreateMessage()
                {
                    return new Message(AcquireRequest, this.RequestData);
                }
            }

            public class Response
            {
                public Response(string result, Data responseData = null)
                {
                    this.Result = result;
                    this.ResponseData = responseData;
                }

                public Response(Message message)
                {
                    this.Result = message.Header;
                    this.ResponseData = message.DeserializeBody<Data>();
                }

                public string Result { get; }

                public Data ResponseData { get; }

                public Message CreateMessage()
                {
                    return new Message(this.Result, this.ResponseData);
                }
            }

            public class Data
            {
                public Data(int pid, string parsedCommand, string originalCommand)
                {
                    this.PID = pid;
                    this.ParsedCommand = parsedCommand;
                    this.OriginalCommand = originalCommand;
                }

                public int PID { get; set; }

                /// <summary>
                /// The command line requesting the lock, built internally for parsing purposes.
                /// e.g. "git status", "git rebase"
                /// </summary>
                public string ParsedCommand { get; set; }

                /// <summary>
                /// The command line for the process requesting the lock, as kept by the OS.
                /// e.g. "c:\bin\git\git.exe git-rebase origin/master"
                /// </summary>
                public string OriginalCommand { get; set; }

                public override string ToString()
                {
                    return this.ParsedCommand + " (" + this.PID + ")";
                }
            }
        }

        public class Message
        {
            public Message(string header, object body)
                : this(header, JsonConvert.SerializeObject(body))
            {
            }

            private Message(string header, string body)
            {
                this.Header = header;
                this.Body = body;
            }

            public string Header { get; }

            public string Body { get; }

            public static Message FromString(string message)
            {
                string header = null;
                string body = null;
                if (!string.IsNullOrEmpty(message))
                {
                    string[] parts = message.Split(new[] { NamedPipeMessages.MessageSeparator }, count: 2);
                    header = parts[0];
                    if (parts.Length > 1)
                    {
                        body = parts[1];
                    }
                }

                return new Message(header, body);
            }

            public TBody DeserializeBody<TBody>()
            {
                if (string.IsNullOrEmpty(this.Body))
                {
                    return default(TBody);
                }

                try
                {
                    return JsonConvert.DeserializeObject<TBody>(this.Body);
                }
                catch (JsonException jsonException)
                {
                    throw new ArgumentException("Unrecognized body contents.", nameof(this.Body), jsonException);
                }
            }

            public override string ToString()
            {
                string result = string.Empty;
                if (!string.IsNullOrEmpty(this.Header))
                {
                    result = this.Header;
                }

                if (this.Body != null)
                {
                    result = result + NamedPipeMessages.MessageSeparator + this.Body;
                }

                return result;
            }
        }
    }
}
