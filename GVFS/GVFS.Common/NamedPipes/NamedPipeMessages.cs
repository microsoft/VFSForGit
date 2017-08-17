using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using Newtonsoft.Json;
using System;
using System.Runtime.Serialization;

namespace GVFS.Common.NamedPipes
{
    /// <summary>
    /// Define messages used to communicate via the named-pipe in GVFS.
    /// </summary>
    /// <remarks>
    /// This class is defined as partial so that GVFS.Hooks 
    /// can compile the portions of it that it cares about (see LockedNamedPipeMessages).
    /// </remarks>
    public static partial class NamedPipeMessages
    {
        public const string UnknownGVFSState = "UnknownGVFSState";

        private const string ResponseSuffix = "Response";

        public enum CompletionState
        {
            NotCompleted,
            Success,
            Failure
        }

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
                public string CacheServer { get; set; }
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

        public static class Notification
        {
            public class Request
            {
                public const string Header = nameof(Notification);

                public string Title { get; set; }

                public string Message { get; set; }

                public static Request FromMessage(Message message)
                {
                    return JsonConvert.DeserializeObject<Request>(message.Body);
                }

                public Message ToMessage()
                {
                    return new Message(Header, JsonConvert.SerializeObject(this));
                }
            }
        }

        public class UnregisterRepoRequest
        {
            public const string Header = nameof(UnregisterRepoRequest);

            public string EnlistmentRoot { get; set; }

            public static UnregisterRepoRequest FromMessage(Message message)
            {
                return JsonConvert.DeserializeObject<UnregisterRepoRequest>(message.Body);
            }

            public Message ToMessage()
            {
                return new Message(Header, JsonConvert.SerializeObject(this));
            }

            public class Response : BaseResponse<UnregisterRepoRequest>
            {
                public static Response FromMessage(Message message)
                {
                    return JsonConvert.DeserializeObject<Response>(message.Body);
                }
            }
        }

        public class RegisterRepoRequest
        {
            public const string Header = nameof(RegisterRepoRequest);

            public string EnlistmentRoot { get; set; }
            public string OwnerSID { get; set; }

            public static RegisterRepoRequest FromMessage(Message message)
            {
                return JsonConvert.DeserializeObject<RegisterRepoRequest>(message.Body);
            }

            public Message ToMessage()
            {
                return new Message(Header, JsonConvert.SerializeObject(this));
            }

            public class Response : BaseResponse<RegisterRepoRequest>
            {
                public static Response FromMessage(Message message)
                {
                    return JsonConvert.DeserializeObject<Response>(message.Body);
                }
            }
        }

        public class AttachGvFltRequest
        {
            public const string Header = nameof(AttachGvFltRequest);

            public string EnlistmentRoot { get; set; }

            public static AttachGvFltRequest FromMessage(Message message)
            {
                return JsonConvert.DeserializeObject<AttachGvFltRequest>(message.Body);
            }

            public Message ToMessage()
            {
                return new Message(Header, JsonConvert.SerializeObject(this));
            }

            public class Response : BaseResponse<AttachGvFltRequest>
            {
                public static Response FromMessage(Message message)
                {
                    return JsonConvert.DeserializeObject<Response>(message.Body);
                }
            }
        }

        public class BaseResponse<TRequest>
        {
            public const string Header = nameof(TRequest) + ResponseSuffix;

            public CompletionState State { get; set; }
            public string ErrorMessage { get; set; }

            public Message ToMessage()
            {
                return new Message(Header, JsonConvert.SerializeObject(this));
            }
        }
    }
}
