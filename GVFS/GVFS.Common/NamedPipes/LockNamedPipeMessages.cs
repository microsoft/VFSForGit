using System;
using System.Collections.Generic;

namespace GVFS.Common.NamedPipes
{
    /// <summary>
    /// Define messages used to communicate via the named-pipe in GVFS.
    /// </summary>
    /// <remarks>
    /// This file contains only the message types that GVFS.Hooks is interested
    /// in using. For all other messages see GVFS.Common/NamedPipeMessages.
    /// </remarks>
    public static partial class NamedPipeMessages
    {
        public const string UnknownRequest = "UnknownRequest";

        private const char MessageSeparator = '|';

        public static class AcquireLock
        {
            public const string AcquireRequest = "AcquireLock";
            public const string DenyGVFSResult = "LockDeniedGVFS";
            public const string DenyGitResult = "LockDeniedGit";
            public const string AcceptResult = "LockAcquired";
            public const string AvailableResult = "LockAvailable";
            public const string MountNotReadyResult = "MountNotReady";
            public const string UnmountInProgressResult = "UnmountInProgress";

            public class Response
            {
                public Response(string result, LockData responseData = null, string denyGVFSMessage = null)
                {
                    this.Result = result;
                    this.ResponseData = responseData;
                    this.DenyGVFSMessage = denyGVFSMessage;
                }

                public Response(Message message)
                {
                    this.Result = message.Header;

                    if (this.Result == DenyGVFSResult)
                    {
                        this.DenyGVFSMessage = message.Body;
                    }
                    else
                    {
                        this.ResponseData = LockData.FromBody(message.Body);
                    }
                }

                public string Result { get; }

                public string DenyGVFSMessage { get; }

                public LockData ResponseData { get; }

                public Message CreateMessage()
                {
                    string messageBody = null;
                    if (this.ResponseData != null)
                    {
                        messageBody = this.ResponseData.ToMessage();
                    }
                    else if (this.DenyGVFSMessage != null)
                    {
                        messageBody = this.DenyGVFSMessage;
                    }

                    return new Message(this.Result, messageBody);
                }
            }
        }

        public static class ReleaseLock
        {
            public const string Request = "ReleaseLock";
            public const string SuccessResult = "LockReleased";
            public const string FailureResult = "ReleaseDenied";

            public class Response
            {
                public Response(string result, ReleaseLockData responseData = null)
                {
                    this.Result = result;
                    this.ResponseData = responseData;
                }

                public Response(Message message)
                {
                    this.Result = message.Header;
                    this.ResponseData = ReleaseLockData.FromBody(message.Body);
                }

                public string Result { get; }

                public ReleaseLockData ResponseData { get; }

                public Message CreateMessage()
                {
                    string messageBody = null;
                    if (this.ResponseData != null)
                    {
                        messageBody = this.ResponseData.ToMessage();
                    }

                    return new Message(this.Result, messageBody);
                }
            }

            public class ReleaseLockData
            {
                // Message Format
                //     FailedUpdateCount<FailedDeleteCount<FailedUpdateList<FailedDeleteList
                //
                //     - If the sum of FailedUpdateCount and FailedDeleteCount exceeds MaxReportedFileNames then
                //       FailedUpdateList and FailedDeleteList will be empty
                //
                //     - Format of each list is Path1|Path2|Path3|...|PathN

                private const char SectionSeparator = '<';
                private const int MaxReportedFileNames = 100;

                public ReleaseLockData(List<string> failedToUpdateFileList, List<string> failedToDeleteFileList)
                    : this(
                          failedToUpdateFileList.Count,
                          failedToDeleteFileList.Count,
                          (failedToUpdateFileList.Count + failedToDeleteFileList.Count <= MaxReportedFileNames) ? failedToUpdateFileList : new List<string>(),
                          (failedToUpdateFileList.Count + failedToDeleteFileList.Count <= MaxReportedFileNames) ? failedToDeleteFileList : new List<string>())
                {
                }

                private ReleaseLockData(
                    int failedToUpdateCount,
                    int failedToDeleteCount,
                    List<string> failedToUpdateFileList,
                    List<string> failedToDeleteFileList)
                {
                    this.FailedToUpdateCount = failedToUpdateCount;
                    this.FailedToDeleteCount = failedToDeleteCount;
                    this.FailedToUpdateFileList = failedToUpdateFileList;
                    this.FailedToDeleteFileList = failedToDeleteFileList;
                }

                public bool HasFailures
                {
                    get
                    {
                        return this.FailedToUpdateCount > 0 || this.FailedToDeleteCount > 0;
                    }
                }

                public int FailedToUpdateCount { get; private set; }
                public int FailedToDeleteCount { get; private set; }
                public bool FailureCountExceedsMaxFileNames
                {
                    get
                    {
                        return (this.FailedToUpdateCount + this.FailedToDeleteCount) > MaxReportedFileNames;
                    }
                }

                public List<string> FailedToUpdateFileList { get; private set; }
                public List<string> FailedToDeleteFileList { get; private set; }

                /// <summary>
                /// Parses ReleaseLockData from the provided string.
                /// </summary>
                /// <param name="body">Message body (containing ReleaseLockData in string format)</param>
                /// <returns>
                /// - ReleaseLockData when body is successfully parsed
                /// - null when there is a parsing error
                /// </returns>
                internal static ReleaseLockData FromBody(string body)
                {
                    if (!string.IsNullOrEmpty(body))
                    {
                        string[] sections = body.Split(new char[] { SectionSeparator });

                        if (sections.Length != 4)
                        {
                            return null;
                        }

                        int failedToUpdateCount;
                        if (!int.TryParse(sections[0], out failedToUpdateCount))
                        {
                            return null;
                        }

                        int failedToDeleteCount;
                        if (!int.TryParse(sections[1], out failedToDeleteCount))
                        {
                            return null;
                        }

                        List<string> failedToUpdateFileList = null;
                        string[] updateParts = sections[2].Split(new char[] { MessageSeparator }, StringSplitOptions.RemoveEmptyEntries);
                        if (updateParts.Length > 0)
                        {
                            failedToUpdateFileList = new List<string>(updateParts);
                        }

                        List<string> failedToDeleteFileList = null;
                        string[] deleteParts = sections[3].Split(new char[] { MessageSeparator }, StringSplitOptions.RemoveEmptyEntries);
                        if (deleteParts.Length > 0)
                        {
                            failedToDeleteFileList = new List<string>(deleteParts);
                        }

                        return new ReleaseLockData(failedToUpdateCount, failedToDeleteCount, failedToUpdateFileList, failedToDeleteFileList);
                    }

                    return new ReleaseLockData(failedToUpdateCount: 0, failedToDeleteCount: 0, failedToUpdateFileList: null, failedToDeleteFileList: null);
                }

                internal string ToMessage()
                {
                    return
                        this.FailedToUpdateCount.ToString() +
                        SectionSeparator +
                        this.FailedToDeleteCount.ToString() +
                        SectionSeparator +
                        string.Join(MessageSeparator.ToString(), this.FailedToUpdateFileList) +
                        SectionSeparator +
                        string.Join(MessageSeparator.ToString(), this.FailedToDeleteFileList);
                }
            }
        }

        public class LockRequest
        {
            public LockRequest(string messageBody)
            {
                this.RequestData = LockData.FromBody(messageBody);
            }

            public LockRequest(int pid, bool isElevated, bool checkAvailabilityOnly, string parsedCommand, string gitCommandSessionId)
            {
                this.RequestData = new LockData(pid, isElevated, checkAvailabilityOnly, parsedCommand, gitCommandSessionId);
            }

            public LockData RequestData { get; }

            public Message CreateMessage(string header)
            {
                return new Message(header, this.RequestData.ToMessage());
            }
        }

        public class LockData
        {
            public LockData(int pid, bool isElevated, bool checkAvailabilityOnly, string parsedCommand, string gitCommandSessionId)
            {
                this.PID = pid;
                this.GitCommandSessionId = gitCommandSessionId;
                this.IsElevated = isElevated;
                this.CheckAvailabilityOnly = checkAvailabilityOnly;
                this.ParsedCommand = parsedCommand;
            }

            public int PID { get; set; }

            public string GitCommandSessionId { get; set; }

            public bool IsElevated { get; set; }

            /// <summary>
            /// Should the command actually acquire the GVFSLock or
            /// only check if the lock is available.
            /// </summary>
            public bool CheckAvailabilityOnly { get; set; }

            /// <summary>
            /// The command line requesting the lock, built internally for parsing purposes.
            /// e.g. "git status", "git rebase"
            /// </summary>
            public string ParsedCommand { get; set; }

            public override string ToString()
            {
                return this.ParsedCommand + " (" + this.PID + ")";
            }

            internal static LockData FromBody(string body)
            {
                if (!string.IsNullOrEmpty(body))
                {
                    // This mesage is stored using the MessageSeperator delimiter for performance reasons
                    // Format of the body uses length prefixed string so that the strings can have the delimiter in them
                    // Examples:
                    // "123|true|false|13|parsedCommand|9|sessionId"
                    // "321|false|true|30|parsedCommand with | delimiter|26|sessionId with | delimiter"
                    string[] dataParts = body.Split(MessageSeparator);
                    int pid;
                    bool isElevated = false;
                    bool checkAvailabilityOnly = false;
                    string parsedCommand = null;

                    if (dataParts.Length < 7)
                    {
                        throw new InvalidOperationException(string.Format("Invalid lock message. Expected at least 7 parts, got: {0} from message: '{1}'", dataParts.Length, body));
                    }

                    if (!int.TryParse(dataParts[0], out pid))
                    {
                        throw new InvalidOperationException(string.Format("Invalid lock message. Expected PID, got: {0} from message: '{1}'", dataParts[0], body));
                    }

                    if (!bool.TryParse(dataParts[1], out isElevated))
                    {
                        throw new InvalidOperationException(string.Format("Invalid lock message. Expected bool for isElevated, got: {0} from message: '{1}'", dataParts[1], body));
                    }

                    if (!bool.TryParse(dataParts[2], out checkAvailabilityOnly))
                    {
                        throw new InvalidOperationException(string.Format("Invalid lock message. Expected bool for checkAvailabilityOnly, got: {0} from message: '{1}'", dataParts[2], body));
                    }

                    if (!int.TryParse(dataParts[3], out int parsedCommandLength))
                    {
                        throw new InvalidOperationException(string.Format("Invalid lock message. Expected command length, got: {0} from message: '{1}'", dataParts[3], body));
                    }

                    // ParsedCommandLength should be the length of the string at the end of the message
                    // Add the length of the previous parts, plus delimiters
                    int commandStartingSpot = dataParts[0].Length + dataParts[1].Length + dataParts[2].Length + dataParts[3].Length + 4;
                    if ((commandStartingSpot + parsedCommandLength) >= body.Length)
                    {
                        throw new InvalidOperationException(string.Format("Invalid lock message. The parsedCommand is an unexpected length, got: {0} from message: '{1}'", parsedCommandLength, body));
                    }

                    parsedCommand = body.Substring(commandStartingSpot, parsedCommandLength);

                    // The session Id is after the parsed command with the length of the session Id string coming first
                    // Use the string after the parsed command string to get the session Id data
                    string sessionIdSubString = body.Substring(commandStartingSpot + parsedCommandLength + 1);
                    string[] sessionIdParts = sessionIdSubString.Split(MessageSeparator);
                    if (!int.TryParse(sessionIdParts[0], out int sessionIdLength))
                    {
                        throw new InvalidOperationException(string.Format("Invalid lock message. Expected session id length, got: {0} from message: '{1}'", sessionIdParts[0], body));
                    }

                    // Validate the session Id data does not exceed the body of the message by using the previous
                    // command starting position and length and adding length of the part for the size of the session id plus the 2 delimiters
                    int sessionIdStartingSpot = commandStartingSpot + parsedCommandLength + sessionIdParts[0].Length + 2;
                    if ((sessionIdStartingSpot + sessionIdLength) != body.Length)
                    {
                        throw new InvalidOperationException(string.Format("Invalid lock message. The sessionId is an unexpected length, got: {0} from message: '{1}'", sessionIdLength, body));
                    }

                    string sessionId = body.Substring(sessionIdStartingSpot, sessionIdLength);

                    return new LockData(pid, isElevated, checkAvailabilityOnly, parsedCommand, sessionId);
                }

                return null;
            }

            internal string ToMessage()
            {
                return string.Join(
                    MessageSeparator.ToString(),
                    this.PID,
                    this.IsElevated,
                    this.CheckAvailabilityOnly,
                    this.ParsedCommand.Length,
                    this.ParsedCommand,
                    this.GitCommandSessionId.Length,
                    this.GitCommandSessionId);
            }
        }

        public class Message
        {
            public Message(string header, string body)
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
