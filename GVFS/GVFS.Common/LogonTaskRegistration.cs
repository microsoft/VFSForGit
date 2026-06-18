using GVFS.Common.Tracing;
using System;
using System.Security.Cryptography;
using System.Text;

namespace GVFS.Common
{
    /// <summary>
    /// Registers / updates / unregisters the machine-wide Windows scheduled task
    /// that mounts registered GVFS enlistments at logon for each interactive user. Replaces
    /// the role of <c>GVFS.Service</c>'s session-change-driven AutoMount in
    /// the user-level install model.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The task is registered at <c>\GVFS\AutoMount</c>, scoped to all
    /// interactive users (GroupId S-1-5-4), runs at user logon with the
    /// user's interactive token, and executes <c>gvfs.exe service --mount-all</c>.
    /// The mount-all verb reads the user's registered repos (via <see cref="LocalRepoRegistry"/>)
    /// and mounts each one.
    /// </para>
    /// <para>
    /// Drift detection works via a content-hash marker embedded in the
    /// task's Description field
    /// (<c>[gvfs-logon-task-hash=XXXXXXXXXXXXXXXX]</c>). The hash covers the
    /// XML template <em>with placeholders still in place</em>, so it is
    /// stable across re-substitutions with different gvfs.exe
    /// paths -- only template content changes (a code change to the
    /// template constant) bump the hash. <see cref="IsCurrent"/> queries
    /// the registered task XML, extracts the marker, and compares against
    /// <see cref="TemplateHash"/>.
    /// </para>
    /// <para>
    /// Tested as a unit by passing a mock <see cref="IScheduledTaskInvoker"/>.
    /// Production callers should use
    /// <see cref="CreateForCurrentPlatform(ITracer)"/>, which constructs a
    /// <see cref="SchTasksScheduledTaskInvoker"/> behind the scenes.
    /// </para>
    /// </remarks>
    public class LogonTaskRegistration
    {
        public const string TaskName = "AutoMount";
        public const string TaskFolder = @"\GVFS\";
        public const string FullTaskPath = @"\GVFS\AutoMount";

        public const string GvfsPathPlaceholder = "__GVFS_PATH__";
        public const string TaskHashPlaceholder = "__TASK_HASH__";

        public const string HashMarkerPrefix = "[gvfs-logon-task-hash=";
        public const string HashMarkerSuffix = "]";

        /// <summary>
        /// Task XML template. Placeholders:
        /// <list type="bullet">
        /// <item><c>__GVFS_PATH__</c> -- absolute path to gvfs.exe</item>
        /// <item><c>__TASK_HASH__</c> -- content hash of this template,
        ///   inserted into the Description for drift detection</item>
        /// </list>
        /// Indented as a verbatim string; the XML emitted is well-formed
        /// and accepted by <c>schtasks /Create /XML</c>.
        /// </summary>
        public const string XmlTemplate =
@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.4"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Author>GVFS</Author>
    <Description>Mounts registered GVFS enlistments at logon for each interactive user. Required by VFS for Git. [gvfs-logon-task-hash=__TASK_HASH__]</Description>
    <URI>\GVFS\AutoMount</URI>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <GroupId>S-1-5-4</GroupId>
      <RunLevel>LeastPrivilege</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT5M</ExecutionTimeLimit>
    <Priority>5</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>conhost.exe</Command>
      <Arguments>--headless __GVFS_PATH__ service --mount-all</Arguments>
    </Exec>
  </Actions>
</Task>
";

        private static readonly Lazy<string> templateHash = new Lazy<string>(ComputeTemplateHash);

        private readonly ITracer tracer;
        private readonly IScheduledTaskInvoker invoker;

        public LogonTaskRegistration(ITracer tracer, IScheduledTaskInvoker invoker)
        {
            ArgumentNullException.ThrowIfNull(tracer);
            ArgumentNullException.ThrowIfNull(invoker);
            this.tracer = tracer;
            this.invoker = invoker;
        }

        /// <summary>
        /// Convenience factory for production callers: wires up a real
        /// <see cref="SchTasksScheduledTaskInvoker"/>.
        /// </summary>
        public static LogonTaskRegistration CreateForCurrentPlatform(ITracer tracer)
        {
            ArgumentNullException.ThrowIfNull(tracer);
            return new LogonTaskRegistration(tracer, new SchTasksScheduledTaskInvoker(tracer));
        }

        /// <summary>
        /// Stable hex hash of <see cref="XmlTemplate"/> (with placeholders
        /// intact). 64 hex chars (full SHA-256), computed once per process.
        /// </summary>
        public static string TemplateHash => templateHash.Value;

        /// <summary>
        /// Substitute placeholders to produce a registerable task XML.
        /// </summary>
        public static string BuildTaskXml(string gvfsExePath)
        {
            ArgumentException.ThrowIfNullOrEmpty(gvfsExePath);

            return XmlTemplate
                .Replace(GvfsPathPlaceholder, gvfsExePath)
                .Replace(TaskHashPlaceholder, TemplateHash);
        }

        /// <summary>
        /// Extract the <c>[gvfs-logon-task-hash=XXXX]</c> hash marker from
        /// arbitrary text (usually a Task's Description). Returns
        /// <c>false</c> when no marker is present.
        /// </summary>
        public static bool TryExtractHashMarker(string text, out string hash)
        {
            hash = null;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            int start = text.IndexOf(HashMarkerPrefix, StringComparison.Ordinal);
            if (start < 0)
            {
                return false;
            }

            int hashStart = start + HashMarkerPrefix.Length;
            int hashEnd = text.IndexOf(HashMarkerSuffix, hashStart, StringComparison.Ordinal);
            if (hashEnd <= hashStart)
            {
                return false;
            }

            hash = text.Substring(hashStart, hashEnd - hashStart);
            return true;
        }

        /// <summary>
        /// Returns <c>true</c> when the logon task is registered AND its
        /// embedded hash marker matches the current template's hash.
        /// Returns <c>false</c> if the task is missing, the query fails,
        /// or the hash differs (drift).
        /// </summary>
        public bool IsCurrent()
        {
            if (!this.invoker.TryQueryXml(FullTaskPath, out string xml, out _))
            {
                return false;
            }

            if (!TryExtractHashMarker(xml, out string registeredHash))
            {
                return false;
            }

            return string.Equals(registeredHash, TemplateHash, StringComparison.Ordinal);
        }

        /// <summary>
        /// Register the logon task with the given gvfs.exe path,
        /// overwriting any existing registration. Idempotent: when
        /// the registered task already matches the intended XML (same
        /// hash, same args), this is a fast no-op.
        /// </summary>
        public bool TryRegisterOrUpdate(string gvfsExePath, out string errorMessage)
        {
            ArgumentException.ThrowIfNullOrEmpty(gvfsExePath);

            if (this.IsCurrent())
            {
                // Still verify args are right; the hash covers the template
                // structure but not the substituted gvfs.exe path. Re-query
                // and check the action command.
                if (this.invoker.TryQueryXml(FullTaskPath, out string existingXml, out _) &&
                    existingXml.Contains(gvfsExePath, StringComparison.Ordinal))
                {
                    errorMessage = string.Empty;
                    return true;
                }
            }

            string xml = BuildTaskXml(gvfsExePath);
            return this.invoker.TryRegisterFromXml(FullTaskPath, xml, out errorMessage);
        }

        /// <summary>
        /// Unregister the logon task. Idempotent: returns <c>true</c> when
        /// the task was unregistered OR was not registered to begin with.
        /// </summary>
        public bool TryUnregister(out string errorMessage)
        {
            return this.invoker.TryUnregister(FullTaskPath, out errorMessage);
        }

        private static string ComputeTemplateHash()
        {
            byte[] bytes = Encoding.UTF8.GetBytes(XmlTemplate);
            byte[] hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash);
        }
    }
}
