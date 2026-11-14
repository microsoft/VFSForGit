using GVFS.Common.Sparse;
using System.Collections.Generic;

namespace GVFS.Common.NamedPipes
{
    public static partial class NamedPipeMessages
    {
        /// <summary>
        /// Messages for automatic sparse-index cone management.
        /// </summary>
        /// <remarks>
        /// The pre-command hook sends a <see cref="WidenRequest"/> before Git runs, so that the
        /// mount can widen the sparse-index cone to cover the paths that the Git command names.
        /// The post-command hook sends a <see cref="NarrowRequest"/> with the same
        /// <c>CommandId</c> after Git runs, so that the mount can remove the cone entries that
        /// it added for that command.
        /// <para>
        /// The hook treats every reply other than <see cref="SuccessResult"/> as "not applied"
        /// and lets Git run. This includes <see cref="UnknownRequest"/> from a mount that does
        /// not handle these messages, <see cref="MountNotReadyResult"/>, no reply within the
        /// wait budget, and no mount. Git then expands the sparse index in memory.
        /// </para>
        /// <para>
        /// A widen request carries only repo-relative git-form paths that the hook resolved
        /// (see <see cref="ConePathspecResolver.IsValidGitPath"/>). It carries no absolute path
        /// and no file for the mount to read. Any local user can write to the mount pipe, so
        /// <see cref="WidenParameters.TryParse"/> rejects a body with any other path.
        /// </para>
        /// <para>
        /// The mount owns the lifetime of the entries that it adds. A narrow can be missing
        /// (the Git process was killed) or late (it timed out). Two Git commands can widen the
        /// same directory at the same time, because commands that do not take the GVFS lock
        /// (for example <c>git diff -- &lt;path&gt;</c>) also send widen requests. The command id
        /// is the Git process id, so the mount can count the commands that need an entry and
        /// remove the entries of a process that no longer runs.
        /// </para>
        /// <para>
        /// A body is a list of fields separated by NUL. The header and the body split on the
        /// first '|' only, so a field can contain '|'.
        /// </para>
        /// </remarks>
        public static class ConeManagement
        {
            public const string WidenRequest = "ConeWiden";
            public const string NarrowRequest = "ConeNarrow";

            /// <summary>The mount applied the request.</summary>
            public const string SuccessResult = "S";

            /// <summary>The mount tried to apply the request and failed.</summary>
            public const string FailureResult = "F";

            /// <summary>The mount received the request, but automatic sparse index is not enabled.</summary>
            public const string NotEnabledResult = "NE";

            /// <summary>
            /// The maximum time that the pre-command hook waits for a widen reply. When the wait
            /// ends without a reply, the hook lets Git run. A fast reply ends the wait early, so
            /// this budget adds latency only when the mount is slow.
            /// </summary>
            public const int WidenWaitBudgetMs = 1000;

            /// <summary>
            /// The maximum time that the post-command hook waits for a narrow reply. Git has
            /// already run, so this wait only delays the return to the user.
            /// </summary>
            public const int NarrowWaitBudgetMs = 100;

            /// <summary>
            /// The maximum time that the hook waits to connect to the mount pipe. A live mount
            /// accepts the connection quickly. Without a mount, the connect fails at this limit.
            /// </summary>
            public const int ConnectTimeoutMs = 50;

            private const char FieldSeparator = '\0';

            /// <summary>
            /// Return true when a command id is empty or has 1 to 10 ASCII digits (a Windows
            /// process id fits in 10 digits).
            /// </summary>
            public static bool IsValidCommandId(string commandId)
            {
                if (commandId == null || commandId.Length > 10)
                {
                    return false;
                }

                foreach (char character in commandId)
                {
                    if (character < '0' || character > '9')
                    {
                        return false;
                    }
                }

                return true;
            }

            /// <summary>
            /// Parameters for a widen request: the Git command id and the repo-relative
            /// git-form paths to cover. A path with a trailing slash is a folder, and the mount
            /// covers its whole subtree. Any other path is a file, and the mount covers the
            /// files in its parent directory.
            /// </summary>
            public class WidenParameters
            {
                public WidenParameters(string commandId, IReadOnlyList<string> paths)
                {
                    this.CommandId = commandId ?? string.Empty;
                    this.Paths = paths ?? new List<string>();
                }

                /// <summary>
                /// Identifies the Git command: the Git process id, as decimal digits, or empty.
                /// The pre-command and post-command hooks of one Git command send the same value.
                /// </summary>
                public string CommandId { get; }

                /// <summary>The repo-relative git-form paths to cover.</summary>
                public IReadOnlyList<string> Paths { get; }

                /// <summary>
                /// Return true when the command id is valid and there is at least one path,
                /// and every path is valid.
                /// </summary>
                public bool IsValid()
                {
                    if (!IsValidCommandId(this.CommandId) || this.Paths.Count == 0)
                    {
                        return false;
                    }

                    foreach (string path in this.Paths)
                    {
                        if (!ConePathspecResolver.IsValidGitPath(path))
                        {
                            return false;
                        }
                    }

                    return true;
                }

                public string ToBody()
                {
                    List<string> fields = new List<string>(1 + this.Paths.Count) { this.CommandId };
                    fields.AddRange(this.Paths);
                    return string.Join(FieldSeparator.ToString(), fields);
                }

                /// <summary>
                /// Parse a widen body. Return false when the body is not valid (see
                /// <see cref="IsValid"/>).
                /// </summary>
                public static bool TryParse(string body, out WidenParameters parameters)
                {
                    parameters = null;
                    if (body == null)
                    {
                        return false;
                    }

                    string[] fields = body.Split(FieldSeparator);
                    List<string> paths = new List<string>(fields.Length - 1);
                    for (int i = 1; i < fields.Length; i++)
                    {
                        paths.Add(fields[i]);
                    }

                    WidenParameters candidate = new WidenParameters(fields[0], paths);
                    if (!candidate.IsValid())
                    {
                        return false;
                    }

                    parameters = candidate;
                    return true;
                }
            }

            /// <summary>
            /// Parameters for a narrow request: the command id of the Git command whose widen
            /// the mount removes.
            /// </summary>
            public class NarrowParameters
            {
                public NarrowParameters(string commandId)
                {
                    this.CommandId = commandId ?? string.Empty;
                }

                /// <summary>The Git process id, as decimal digits, or empty.</summary>
                public string CommandId { get; }

                public string ToBody()
                {
                    return this.CommandId;
                }

                /// <summary>
                /// Parse a narrow body. Return false when the command id is not valid.
                /// </summary>
                public static bool TryParse(string body, out NarrowParameters parameters)
                {
                    parameters = null;
                    if (!IsValidCommandId(body))
                    {
                        return false;
                    }

                    parameters = new NarrowParameters(body);
                    return true;
                }
            }
        }
    }
}
