using GVFS.Common;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace GVFS.Platform.Windows
{
    public class CurrentUser : IDisposable
    {
        private const int TokenPrimary = 1;

        private const uint DuplicateTokenFlags = (uint)(TokenRights.Query | TokenRights.AssignPrimary | TokenRights.Duplicate | TokenRights.Default | TokenRights.SessionId);

        private const int StartInfoUseStdHandles = 0x00000100;
        private const uint HandleFlagInherit = 1;

        private readonly ITracer tracer;
        private readonly IntPtr token;

        public CurrentUser(ITracer tracer, int sessionId)
        {
            this.tracer = tracer;
            this.token = GetCurrentUserToken(this.tracer, sessionId);
            if (this.token != IntPtr.Zero)
            {
                this.Identity = new WindowsIdentity(this.token);
            }
            else
            {
                this.Identity = null;
            }
        }

        private enum TokenRights : uint
        {
            StandardRightsRequired = 0x000F0000,
            StandardRightsRead = 0x00020000,
            AssignPrimary = 0x0001,
            Duplicate = 0x0002,
            TokenImpersonate = 0x0004,
            Query = 0x0008,
            QuerySource = 0x0010,
            AdjustPrivileges = 0x0020,
            AdjustGroups = 0x0040,
            Default = 0x0080,
            SessionId = 0x0100,
            Read = (StandardRightsRead | Query),
            AllAccess = (StandardRightsRequired | AssignPrimary |
                Duplicate | TokenImpersonate | Query | QuerySource |
                AdjustPrivileges | AdjustGroups | Default |
                SessionId),
        }

        private enum SECURITY_IMPERSONATION_LEVEL
        {
            SecurityAnonymous,
            SecurityIdentification,
            SecurityImpersonation,
            SecurityDelegation
        }

        private enum WaitForObjectResults : uint
        {
            WaitSuccess = 0,
            WaitAbandoned = 0x80,
            WaitTimeout = 0x102,
            WaitFailed = 0xFFFFFFFF
        }

        private enum ConnectionState
        {
            Active,
            Connected,
            ConnectQuery,
            Shadowing,
            Disconnected,
            Idle,
            Listening,
            Reset,
            Down,
            Initializing
        }

        [Flags]
        private enum CreateProcessFlags : uint
        {
            CREATE_BREAKAWAY_FROM_JOB = 0x01000000,
            CREATE_DEFAULT_ERROR_MODE = 0x04000000,
            CREATE_NEW_CONSOLE = 0x00000010,
            CREATE_NEW_PROCESS_GROUP = 0x00000200,
            CREATE_NO_WINDOW = 0x08000000,
            CREATE_PROTECTED_PROCESS = 0x00040000,
            CREATE_PRESERVE_CODE_AUTHZ_LEVEL = 0x02000000,
            CREATE_SEPARATE_WOW_VDM = 0x00000800,
            CREATE_SHARED_WOW_VDM = 0x00001000,
            CREATE_SUSPENDED = 0x00000004,
            CREATE_UNICODE_ENVIRONMENT = 0x00000400,
            DEBUG_ONLY_THIS_PROCESS = 0x00000002,
            DEBUG_PROCESS = 0x00000001,
            DETACHED_PROCESS = 0x00000008,
            EXTENDED_STARTUPINFO_PRESENT = 0x00080000,
            INHERIT_PARENT_AFFINITY = 0x00010000
        }

        public WindowsIdentity Identity { get; }

        /// <summary>
        /// Launches a process for the current user.
        /// This code will only work when running in a windows service running
        /// as LocalSystem with the SE_TCB_NAME privilege.
        /// </summary>
        /// <param name="processName">Full path to the executable to launch.</param>
        /// <param name="arguments">
        /// Argument values exactly as the child process should see them in its
        /// <c>argv</c>. Each value is escaped according to
        /// <c>CommandLineToArgvW</c> rules so embedded quotes, spaces, and
        /// backslashes round-trip safely. Passing pre-concatenated argument
        /// strings here would re-introduce the quote-stripping bug that
        /// silently corrupts the service's <c>--internal_use_only</c> JSON.
        /// </param>
        /// <param name="processId">
        /// On success, the PID of the newly created process so the caller can
        /// detect early termination (e.g. via
        /// <see cref="System.Diagnostics.Process.GetProcessById(int)"/>)
        /// instead of waiting the full pipe-connection timeout when the child
        /// has already crashed.
        /// </param>
        /// <returns><c>true</c> if the process was successfully created.</returns>
        public bool TryRunAs(string processName, string[] arguments, out int processId)
        {
            processId = 0;
            IntPtr environment = IntPtr.Zero;
            IntPtr duplicate = IntPtr.Zero;
            if (this.token == IntPtr.Zero)
            {
                return false;
            }

            string commandLine = BuildCommandLine(processName, arguments);

            try
            {
                if (DuplicateTokenEx(
                    this.token,
                    DuplicateTokenFlags,
                    IntPtr.Zero,
                    SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation,
                    TokenPrimary,
                    out duplicate))
                {
                    if (CreateEnvironmentBlock(ref environment, duplicate, false))
                    {
                        STARTUP_INFO info = new STARTUP_INFO();
                        info.Length = Marshal.SizeOf<STARTUP_INFO>();

                        PROCESS_INFORMATION procInfo = new PROCESS_INFORMATION();
                        if (CreateProcessAsUser(
                            duplicate,
                            null,
                            commandLine,
                            IntPtr.Zero,
                            IntPtr.Zero,
                            inheritHandles: false,
                            creationFlags: CreateProcessFlags.CREATE_NO_WINDOW | CreateProcessFlags.CREATE_UNICODE_ENVIRONMENT,
                            environment: environment,
                            currentDirectory: null,
                            startupInfo: ref info,
                            processInformation: out procInfo))
                        {
                            try
                            {
                                processId = procInfo.ProcessId;
                                this.tracer.RelatedInfo("Started process '{0}' with Id {1}", commandLine, processId);
                            }
                            finally
                            {
                                CloseHandle(procInfo.ProcessHandle);
                                CloseHandle(procInfo.ThreadHandle);
                            }

                            return true;
                        }
                        else
                        {
                            TraceWin32Error(this.tracer, "Unable to start process.");
                        }
                    }
                    else
                    {
                        TraceWin32Error(this.tracer, "Unable to set child process environment block.");
                    }
                }
                else
                {
                    TraceWin32Error(this.tracer, "Unable to duplicate user token.");
                }
            }
            finally
            {
                if (environment != IntPtr.Zero)
                {
                    DestroyEnvironmentBlock(environment);
                }

                if (duplicate != IntPtr.Zero)
                {
                    CloseHandle(duplicate);
                }
            }

            return false;
        }

        private static string BuildCommandLine(string processName, string[] arguments)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(CommandLineEscaping.EscapeArgument(processName));
            if (arguments != null)
            {
                foreach (string argument in arguments)
                {
                    builder.Append(' ');
                    builder.Append(CommandLineEscaping.EscapeArgument(argument));
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// Returns session IDs for sessions that have a logged-in user
        /// whose token can be queried via <c>WTSQueryUserToken</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// WTS session states relevant to telemetry pipe attachment:
        /// </para>
        /// <list type="table">
        /// <listheader><term>State</term><description>Meaning</description></listheader>
        /// <item><term>Active</term><description>
        ///   User logged in, session connected (console or RDP).
        ///   Has a valid user token. This is the primary case.
        /// </description></item>
        /// <item><term>Connected</term><description>
        ///   Client attached but no user logged in (e.g. the console
        ///   session showing the Windows login screen on a Cloud PC).
        ///   No user token — <c>WTSQueryUserToken</c> will fail.
        /// </description></item>
        /// <item><term>Disconnected</term><description>
        ///   User logged in but client disconnected (e.g. closed RDP
        ///   window without logging off). The user's profile, processes,
        ///   and token are still available. Included so the service can
        ///   attach telemetry even when no client is actively connected.
        /// </description></item>
        /// </list>
        /// <para>
        /// Session 0 is the services session and never has a user token,
        /// even when its state is Disconnected. Excluded by the ID > 0
        /// guard.
        /// </para>
        /// </remarks>
        public static List<int> GetInteractiveSessionIds(ITracer tracer)
        {
            List<int> sessionIds = new List<int>();
            List<WTS_SESSION_INFO> sessions = ListSessions(tracer);
            foreach (WTS_SESSION_INFO session in sessions)
            {
                if (session.SessionID > 0 &&
                    (session.State == ConnectionState.Active ||
                     session.State == ConnectionState.Disconnected))
                {
                    sessionIds.Add(session.SessionID);
                }
            }

            return sessionIds;
        }

        public void Dispose()
        {
            if (this.token != IntPtr.Zero)
            {
                CloseHandle(this.token);
            }
        }

        private static void TraceWin32Error(ITracer tracer, string preface)
        {
            Win32Exception e = new Win32Exception(Marshal.GetLastWin32Error());
            tracer.RelatedError(preface + " Exception: " + e.Message);
        }

        private static IntPtr GetCurrentUserToken(ITracer tracer, int sessionId)
        {
            IntPtr output = IntPtr.Zero;
            if (WTSQueryUserToken((uint)sessionId, out output))
            {
                return output;
            }
            else
            {
                // Warning, not error: sessions without a logged-in user
                // (e.g. the console session on a Cloud PC) are expected.
                Win32Exception e = new Win32Exception(Marshal.GetLastWin32Error());
                tracer.RelatedWarning("Unable to query user token from session '{0}'. Exception: {1}", sessionId, e.Message);
            }

            return IntPtr.Zero;
        }

        private static List<WTS_SESSION_INFO> ListSessions(ITracer tracer)
        {
            IntPtr sessionInfo = IntPtr.Zero;
            IntPtr server = IntPtr.Zero;
            List<WTS_SESSION_INFO> output = new List<WTS_SESSION_INFO>();

            try
            {
                int count = 0;
                int retval = WTSEnumerateSessions(IntPtr.Zero, 0, 1, ref sessionInfo, ref count);
                if (retval != 0)
                {
                    int dataSize = Marshal.SizeOf<WTS_SESSION_INFO>();
                    long current = sessionInfo.ToInt64();

                    for (int i = 0; i < count; i++)
                    {
                        WTS_SESSION_INFO si = Marshal.PtrToStructure<WTS_SESSION_INFO>((IntPtr)current);
                        current += dataSize;

                        output.Add(si);
                    }
                }
                else
                {
                    TraceWin32Error(tracer, "Unable to enumerate sessions on the current host.");
                }
            }
            catch (Exception exception)
            {
                output.Clear();
                tracer.RelatedError(exception.ToString());
            }
            finally
            {
                if (sessionInfo != IntPtr.Zero)
                {
                    WTSFreeMemory(sessionInfo);
                }
            }

            return output;
        }

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll")]
        private static extern WaitForObjectResults WaitForSingleObject(IntPtr handle, uint timeout = uint.MaxValue);

        [DllImport("wtsapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int WTSEnumerateSessions(IntPtr server, int reserved, int version, ref IntPtr sessionInfo, ref int count);

        [DllImport("advapi32.dll", EntryPoint = "CreateProcessAsUserW", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool CreateProcessAsUser(
            IntPtr token,
            string applicationName,
            string commandLine,
            IntPtr processAttributes,
            IntPtr threadAttributes,
            bool inheritHandles,
            CreateProcessFlags creationFlags,
            IntPtr environment,
            string currentDirectory,
            ref STARTUP_INFO startupInfo,
            out PROCESS_INFORMATION processInformation);

        [DllImport("wtsapi32.dll")]
        private static extern void WTSFreeMemory(IntPtr memory);

        [DllImport("wtsapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

        [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool DuplicateTokenEx(
            IntPtr existingToken,
            uint desiredAccess,
            IntPtr tokenAttributes,
            SECURITY_IMPERSONATION_LEVEL impersonationLevel,
            int tokenType,
            out IntPtr newToken);

        [DllImport("userenv.dll", SetLastError = true)]
        private static extern bool CreateEnvironmentBlock(ref IntPtr environment, IntPtr token, bool inherit);

        [DllImport("userenv.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyEnvironmentBlock(IntPtr environment);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUP_INFO
        {
            public int Length;
            public string Reserved;
            public string DesktopName;
            public string Title;
            public int WindowX;
            public int WindowY;
            public int WindowWidth;
            public int WindowHeight;
            public int ConsoleBufferWidth;
            public int ConsoleBufferHeight;
            public int ConsoleColors;
            public int Flags;
            public short ShowWindow;
            public short Reserved2;
            public IntPtr Reserved3;
            public IntPtr StdInput;
            public IntPtr StdOutput;
            public IntPtr StdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SECURITY_ATTRIBUTES
        {
            public int Length;
            public IntPtr SecurityDescriptor;
            public bool InheritHandle;
        }

        [StructLayoutAttribute(LayoutKind.Sequential)]
        private struct SECURITY_DESCRIPTOR
        {
            public byte Revision;
            public byte Size;
            public short Control;
            public IntPtr Owner;
            public IntPtr Group;
            public IntPtr Sacl;
            public IntPtr Dacl;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr ProcessHandle;
            public IntPtr ThreadHandle;
            public int ProcessId;
            public int ThreadId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WTS_SESSION_INFO
        {
            public int SessionID;

            [MarshalAs(UnmanagedType.LPTStr)]
            public string WinStationName;
            public ConnectionState State;
        }
    }
}