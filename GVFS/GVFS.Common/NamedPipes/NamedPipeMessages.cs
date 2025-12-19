using Newtonsoft.Json;
using System;
using System.Collections.Generic;

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
        public const string MountNotReadyResult = "MountNotReady";

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
                public string LocalCacheRoot { get; set; }
                public string RepoUrl { get; set; }
                public string CacheServer { get; set; }
                public int BackgroundOperationCount { get; set; }
                public string LockStatus { get; set; }
                public string DiskLayoutVersion { get; set; }

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

        public static class ModifiedPaths
        {
            public const string ListRequest = "MPL";
            public const string InvalidVersion = "InvalidVersion";
            public const string SuccessResult = "S";
            public const string CurrentVersion = "1";

            public class Request
            {
                public Request(Message message)
                {
                    this.Version = message.Body;
                }

                public string Version { get; }
            }

            public class Response
            {
                public Response(string result, string data = "")
                {
                    this.Result = result;
                    this.Data = data;
                }

                public string Result { get; }
                public string Data { get; }

                public Message CreateMessage()
                {
                    return new Message(this.Result, this.Data);
                }
            }
        }

        public static class DownloadObject
        {
            public const string DownloadRequest = "DLO";
            public const string SuccessResult = "S";
            public const string DownloadFailed = "F";
            public const string InvalidSHAResult = "InvalidSHA";

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

        public static class PostIndexChanged
        {
            public const string NotificationRequest = "PICN";
            public const string SuccessResult = "S";
            public const string FailureResult = "F";

            public class Request
            {
                public Request(Message message)
                {
                    if (message.Body.Length != 2)
                    {
                        throw new InvalidOperationException($"Invalid PostIndexChanged message. Expected 2 characters, got: {message.Body.Length} from message: '{message.Body}'");
                    }

                    this.UpdatedWorkingDirectory = message.Body[0] == '1';
                    this.UpdatedSkipWorktreeBits = message.Body[1] == '1';
                }

                public Request(bool updatedWorkingDirectory, bool updatedSkipWorktreeBits)
                {
                    this.UpdatedWorkingDirectory = updatedWorkingDirectory;
                    this.UpdatedSkipWorktreeBits = updatedSkipWorktreeBits;
                }

                public bool UpdatedWorkingDirectory { get; }

                public bool UpdatedSkipWorktreeBits { get; }

                public Message CreateMessage()
                {
                    return new Message(NotificationRequest, $"{this.BoolToString(this.UpdatedWorkingDirectory)}{this.BoolToString(this.UpdatedSkipWorktreeBits)}");
                }

                private string BoolToString(bool value)
                {
                    return value ? "1" : "0";
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

        public static class DehydrateFolders
        {
            public const string Dehydrate = "Dehydrate";
            public const string DehydratedResult = "Dehydrated";
            public const string MountNotReadyResult = "MountNotReady";

            public class Request
            {
                public Request(string backupFolderPath, string folders)
                {
                    this.Folders = folders;
                    this.BackupFolderPath = backupFolderPath;
                }

                public static Request FromMessage(Message message)
                {
                    return JsonConvert.DeserializeObject<Request>(message.Body);
                }

                public string Folders { get; }

                public string BackupFolderPath { get; }

                public Message CreateMessage()
                {
                    return new Message(Dehydrate, JsonConvert.SerializeObject(this));
                }
            }

            public class Response
            {
                public Response(string result)
                {
                    this.Result = result;
                    this.SuccessfulFolders = new List<string>();
                    this.FailedFolders = new List<string>();
                }

                public string Result { get; }
                public List<string> SuccessfulFolders { get; }
                public List<string> FailedFolders { get; }

                public static Response FromMessage(Message message)
                {
                    return JsonConvert.DeserializeObject<Response>(message.Body);
                }

                public Message CreateMessage()
                {
                    return new Message(this.Result, JsonConvert.SerializeObject(this));
                }
            }
        }

        public static class RunPostFetchJob
        {
            public const string PostFetchJob = "PostFetch";
            public const string QueuedResult = "Queued";
            public const string MountNotReadyResult = "MountNotReady";

            public class Request
            {
                public Request(List<string> packIndexes)
                {
                    this.PackIndexList = JsonConvert.SerializeObject(packIndexes);
                }

                public Request(Message message)
                {
                    this.PackIndexList = message.Body;
                }

                /// <summary>
                /// The PackIndexList data is a JSON-formatted list of strings,
                /// where each string is the name of an IDX file in the shared
                /// object cache.
                /// </summary>
                public string PackIndexList { get; set; }

                public Message CreateMessage()
                {
                    return new Message(PostFetchJob, this.PackIndexList);
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

                public enum Identifier
                {
                    AutomountStart,
                    MountSuccess,
                    MountFailure,
                    UpgradeAvailable
                }

                public Identifier Id { get; set; }

                public string Title { get; set; }

                public string Message { get; set; }

                public string Enlistment { get; set; }

                public int EnlistmentCount { get; set; }

                public string NewVersion { get; set; }

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

        public class EnableAndAttachProjFSRequest
        {
            public const string Header = nameof(EnableAndAttachProjFSRequest);

            public string EnlistmentRoot { get; set; }

            public static EnableAndAttachProjFSRequest FromMessage(Message message)
            {
                return JsonConvert.DeserializeObject<EnableAndAttachProjFSRequest>(message.Body);
            }

            public Message ToMessage()
            {
                return new Message(Header, JsonConvert.SerializeObject(this));
            }

            public class Response : BaseResponse<EnableAndAttachProjFSRequest>
            {
                public static Response FromMessage(Message message)
                {
                    return JsonConvert.DeserializeObject<Response>(message.Body);
                }
            }
        }

        public class GetActiveRepoListRequest
        {
            public const string Header = nameof(GetActiveRepoListRequest);

            public static GetActiveRepoListRequest FromMessage(Message message)
            {
                return JsonConvert.DeserializeObject<GetActiveRepoListRequest>(message.Body);
            }

            public Message ToMessage()
            {
                return new Message(Header, JsonConvert.SerializeObject(this));
            }

            public class Response : BaseResponse<GetActiveRepoListRequest>
            {
                public List<string> RepoList { get; set; }

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
