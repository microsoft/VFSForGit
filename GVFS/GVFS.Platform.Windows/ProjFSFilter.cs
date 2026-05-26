using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using Microsoft.Win32;
using Microsoft.Windows.ProjFS;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.ServiceProcess;
using System.Text;

namespace GVFS.Platform.Windows
{
    public class ProjFSFilter : IKernelDriver
    {
        public const string ServiceName = "PrjFlt";
        private const string DriverName = "prjflt";
        private const string DriverFileName = DriverName + ".sys";
        private const string OptionalFeatureName = "Client-ProjFS";
        private const string EtwArea = nameof(ProjFSFilter);

        private const string PrjFltAutoLoggerKey = "SYSTEM\\CurrentControlSet\\Control\\WMI\\Autologger\\Microsoft-Windows-ProjFS-Filter-Log";
        private const string PrjFltAutoLoggerStartValue = "Start";

        private const string System32LogFilesRoot = @"%SystemRoot%\System32\LogFiles";

        // From "Autologger" section of prjflt.inf
        private const string FilterLoggerGuid = "ee4206ff-4a4d-452f-be56-6bd0ed272b44";
        private const string FilterLoggerSessionName = "Microsoft-Windows-ProjFS-Filter-Log";

        private const string ProjFSNativeLibFileName = "ProjectedFSLib.dll";
        private const string ProjFSManagedLibFileName = "ProjectedFSLib.Managed.dll";

        private const uint OkResult = 0;
        private const uint NameCollisionErrorResult = 0x801F0012;
        private const uint AccessDeniedResult = 0x80070005;

        private enum ProjFSInboxStatus
        {
            Invalid,
            NotInbox = 2,
            Enabled = 3,
            Disabled = 4,
        }

        public bool EnumerationExpandsDirectories { get; } = false;
        public bool EmptyPlaceholdersRequireFileSize { get; } = true;

        public string LogsFolderPath
        {
            get
            {
                return Path.Combine(Environment.ExpandEnvironmentVariables(System32LogFilesRoot), ProjFSFilter.ServiceName);
            }
        }

        public static bool TryAttach(string enlistmentRoot, out string errorMessage)
        {
            errorMessage = null;
            try
            {
                StringBuilder volumePathName = new StringBuilder(GVFSConstants.MaxPath);
                if (!NativeMethods.GetVolumePathName(enlistmentRoot, volumePathName, GVFSConstants.MaxPath))
                {
                    errorMessage = "Could not get volume path name";
                    return false;
                }

                uint result = NativeMethods.FilterAttach(DriverName, volumePathName.ToString(), null);
                if (result != OkResult && result != NameCollisionErrorResult)
                {
                    errorMessage = string.Format("Attaching the filter driver resulted in: {0}", result);
                    return false;
                }
            }
            catch (Exception e)
            {
                errorMessage = string.Format("Attaching the filter driver resulted in: {0}", e.Message);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Attempts to attach the ProjFS filter driver to the volume containing the enlistment.
        /// If FilterAttach returns ACCESS_DENIED but the ProjFS service is already running,
        /// the filter is presumed to already be attached and the call succeeds.
        /// </summary>
        public static bool TryAttachToVolume(string enlistmentRoot, ITracer tracer, out string errorMessage)
        {
            if (TryAttach(enlistmentRoot, out errorMessage))
            {
                return true;
            }

            // FilterAttach requires SE_LOAD_DRIVER_PRIVILEGE, which is typically only
            // granted to administrators. When the caller lacks this privilege but the
            // ProjFS service is confirmed running, the filter is probably already
            // attached to the volume — treat ACCESS_DENIED as success in that case.
            if (errorMessage != null
                && errorMessage.Contains(AccessDeniedResult.ToString())
                && IsServiceRunning(tracer))
            {
                tracer.RelatedInfo($"{nameof(TryAttachToVolume)}: FilterAttach returned ACCESS_DENIED, but ProjFS service is running. Proceeding.");
                errorMessage = null;
                return true;
            }

            return false;
        }

        public static bool IsServiceRunning(ITracer tracer)
        {
            try
            {
                ServiceController controller = new ServiceController(DriverName);
                return controller.Status.Equals(ServiceControllerStatus.Running);
            }
            catch (InvalidOperationException e)
            {
                if (tracer != null)
                {
                    EventMetadata metadata = CreateEventMetadata();
                    metadata.Add("Exception", e.Message);
                    metadata.Add(TracingConstants.MessageKey.InfoMessage, $"{nameof(IsServiceRunning)}: InvalidOperationException: {ServiceName} service was not found");
                    tracer.RelatedEvent(EventLevel.Informational, $"{nameof(IsServiceRunning)}_ServiceNotFound", metadata);
                }

                return false;
            }
        }

        public static bool IsServiceRunningAndInstalled(
            ITracer tracer,
            PhysicalFileSystem fileSystem,
            out bool isPrjfltServiceInstalled,
            out bool isPrjfltDriverInstalled,
            out bool isNativeProjFSLibInstalled)
        {
            bool isRunning = false;
            isPrjfltServiceInstalled = false;
            isPrjfltDriverInstalled = fileSystem.FileExists(Path.Combine(Environment.SystemDirectory, "drivers", DriverFileName));
            isNativeProjFSLibInstalled = IsNativeLibInstalled(tracer, fileSystem);

            try
            {
                ServiceController controller = new ServiceController(DriverName);
                isRunning = controller.Status.Equals(ServiceControllerStatus.Running);
                isPrjfltServiceInstalled = true;
            }
            catch (InvalidOperationException e)
            {
                if (tracer != null)
                {
                    EventMetadata metadata = CreateEventMetadata();
                    metadata.Add("Exception", e.Message);
                    metadata.Add(TracingConstants.MessageKey.InfoMessage, $"{nameof(IsServiceRunningAndInstalled)}: InvalidOperationException: {ServiceName} service was not found");
                    tracer.RelatedEvent(EventLevel.Informational, $"{nameof(IsServiceRunningAndInstalled)}_ServiceNotFound", metadata);
                }

                return false;
            }

            return isRunning;
        }

        public static bool TryStartService(ITracer tracer)
        {
            try
            {
                ServiceController controller = new ServiceController(DriverName);
                if (!controller.Status.Equals(ServiceControllerStatus.Running))
                {
                    controller.Start();
                }

                return true;
            }
            catch (InvalidOperationException e)
            {
                EventMetadata metadata = CreateEventMetadata(e);
                tracer.RelatedError(metadata, $"{nameof(TryStartService)}: InvalidOperationException: {ServiceName} Service was not found");
            }
            catch (Win32Exception e)
            {
                EventMetadata metadata = CreateEventMetadata(e);
                tracer.RelatedError(metadata, $"{nameof(TryStartService)}: Win32Exception while trying to start prjflt");
            }

            return false;
        }

        public static bool IsAutoLoggerEnabled(ITracer tracer)
        {
            object startValue;

            try
            {
                startValue = WindowsPlatform.GetValueFromRegistry(RegistryHive.LocalMachine, PrjFltAutoLoggerKey, PrjFltAutoLoggerStartValue);

                if (startValue == null)
                {
                    tracer.RelatedError($"{nameof(IsAutoLoggerEnabled)}: Failed to find current Start value setting");
                    return false;
                }
            }
            catch (UnauthorizedAccessException e)
            {
                EventMetadata metadata = CreateEventMetadata(e);
                tracer.RelatedError(metadata, $"{nameof(IsAutoLoggerEnabled)}: UnauthorizedAccessException caught while trying to determine if auto-logger is enabled");
                return false;
            }
            catch (SecurityException e)
            {
                EventMetadata metadata = CreateEventMetadata(e);
                tracer.RelatedError(metadata, $"{nameof(IsAutoLoggerEnabled)}: SecurityException caught while trying to determine if auto-logger is enabled");
                return false;
            }

            try
            {
                return Convert.ToInt32(startValue) == 1;
            }
            catch (Exception e)
            {
                EventMetadata metadata = CreateEventMetadata(e);
                metadata.Add(nameof(startValue), startValue);
                tracer.RelatedError(metadata, $"{nameof(IsAutoLoggerEnabled)}: Exception caught while trying to determine if auto-logger is enabled");
                return false;
            }
        }

        public static bool TryEnableAutoLogger(ITracer tracer)
        {
            try
            {
                if (WindowsPlatform.GetValueFromRegistry(RegistryHive.LocalMachine, PrjFltAutoLoggerKey, PrjFltAutoLoggerStartValue) != null)
                {
                    if (WindowsPlatform.TrySetDWordInRegistry(RegistryHive.LocalMachine, PrjFltAutoLoggerKey, PrjFltAutoLoggerStartValue, 1))
                    {
                        return true;
                    }
                }
            }
            catch (UnauthorizedAccessException e)
            {
                EventMetadata metadata = CreateEventMetadata(e);
                tracer.RelatedError(metadata, $"{nameof(TryEnableAutoLogger)}: UnauthorizedAccessException caught while trying to enable auto-logger");
            }
            catch (SecurityException e)
            {
                EventMetadata metadata = CreateEventMetadata(e);
                tracer.RelatedError(metadata, $"{nameof(TryEnableAutoLogger)}: SecurityException caught while trying to enable auto-logger");
            }

            tracer.RelatedError($"{nameof(TryEnableAutoLogger)}: Failed to find AutoLogger Start value in registry");
            return false;
        }

        public static bool TryEnableOrInstallDriver(
            ITracer tracer,
            PhysicalFileSystem fileSystem,
            out uint windowsBuildNumber,
            out bool isInboxProjFSFinalAPI,
            out bool isProjFSFeatureAvailable)
        {
            isProjFSFeatureAvailable = false;

            // Log build number for telemetry. ProjFS is inbox on all supported OS versions
            // (Windows 10 1809 / build 17763+), so this is informational only.
            if (!TryGetWindowsBuildNumber(tracer, out windowsBuildNumber))
            {
                tracer.RelatedWarning($"{nameof(TryEnableOrInstallDriver)}: Could not determine Windows build number");
            }

            isInboxProjFSFinalAPI = true;

            if (TryEnableProjFSOptionalFeature(tracer, fileSystem, out isProjFSFeatureAvailable))
            {
                return true;
            }

            return false;
        }

        public static bool IsNativeLibInstalled(ITracer tracer, PhysicalFileSystem fileSystem)
        {
            string system32Path = Path.Combine(Environment.SystemDirectory, ProjFSNativeLibFileName);
            bool existsInSystem32 = fileSystem.FileExists(system32Path);

            EventMetadata metadata = CreateEventMetadata();
            metadata.Add(nameof(system32Path), system32Path);
            metadata.Add(nameof(existsInSystem32), existsInSystem32);

            // Check for stale app-local native library from legacy non-inbox installs.
            // This file should not exist on current builds; warn if found so admins can clean it up.
            string appLocalPath = Path.Combine(ProcessHelper.GetCurrentProcessLocation(), ProjFSNativeLibFileName);
            bool staleAppLocalLibExists = fileSystem.FileExists(appLocalPath);
            if (staleAppLocalLibExists)
            {
                metadata.Add(nameof(appLocalPath), appLocalPath);
                metadata.Add(TracingConstants.MessageKey.WarningMessage, "Stale app-local ProjectedFSLib.dll found from legacy non-inbox ProjFS install");
            }

            tracer.RelatedEvent(EventLevel.Informational, nameof(IsNativeLibInstalled), metadata);
            return existsInSystem32;
        }

        public bool IsGVFSUpgradeSupported()
        {
            return IsInboxAndEnabled();
        }

        public bool IsSupported(string normalizedEnlistmentRootPath, out string warning, out string error)
        {
            warning = null;
            error = null;

            string pathRoot = Path.GetPathRoot(normalizedEnlistmentRootPath);
            DriveInfo rootDriveInfo = DriveInfo.GetDrives().FirstOrDefault(x => x.Name == pathRoot);
            string[] requiredFormats = new[] { "NTFS", "ReFS" };
            if (rootDriveInfo == null)
            {
                warning = $"Unable to ensure that '{normalizedEnlistmentRootPath}' is an {string.Join(" or ", requiredFormats)} volume.";
            }
            else if (!requiredFormats.Any(requiredFormat => string.Equals(rootDriveInfo.DriveFormat, requiredFormat, StringComparison.OrdinalIgnoreCase)))
            {
                error = $"Only {string.Join(" and ", requiredFormats)} volumes are supported.  Ensure your repo is located in an {string.Join(" or ", requiredFormats)} volume.";
                return false;
            }

            if (Common.NativeMethods.IsFeatureSupportedByVolume(
                Directory.GetDirectoryRoot(normalizedEnlistmentRootPath),
                Common.NativeMethods.FileSystemFlags.FILE_RETURNS_CLEANUP_RESULT_INFO))
            {
                return true;
            }

            error = "File system does not support features required by VFS for Git. Confirm that Windows version is at or beyond that required by VFS for Git. A one-time reboot is required on Windows Server 2016 after installing VFS for Git.";
            return false;
        }

        public bool TryFlushLogs(out string error)
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                string logfileName;
                uint result = Common.NativeMethods.FlushTraceLogger(FilterLoggerSessionName, FilterLoggerGuid, out logfileName);
                if (result != 0)
                {
                    sb.AppendFormat($"Failed to flush {ProjFSFilter.ServiceName} log buffers {result}");
                    error = sb.ToString();
                    return false;
                }
            }
            catch (Exception e)
            {
                sb.AppendFormat($"Failed to flush {ProjFSFilter.ServiceName} log buffers, exception: {e.ToString()}");
                error = sb.ToString();
                return false;
            }

            error = sb.ToString();
            return true;
        }

        public bool TryPrepareFolderForCallbacks(string folderPath, out string error, out Exception exception)
        {
            exception = null;
            try
            {
                return this.TryPrepareFolderForCallbacksImpl(folderPath, out error);
            }
            catch (FileNotFoundException e)
            {
                exception = e;

                if (e.FileName.Equals(ProjFSManagedLibFileName, GVFSPlatform.Instance.Constants.PathComparison))
                {
                    error = $"Failed to load {ProjFSManagedLibFileName}. Ensure that ProjFS is installed and enabled";
                }
                else
                {
                    error = $"FileNotFoundException while trying to prepare \"{folderPath}\" for callbacks: {e.Message}";
                }

                return false;
            }
            catch (Exception e)
            {
                exception = e;
                error = $"Exception while trying to prepare \"{folderPath}\" for callbacks: {e.Message}";
                return false;
            }
        }

        // TODO 1050199: Once the service is an optional component, GVFS should only attempt to attach
        // the filter via the service if the service is present\enabled
        public bool IsReady(JsonTracer tracer, string enlistmentRoot, TextWriter output, out string error)
        {
            error = string.Empty;
            if (!IsServiceRunning(tracer))
            {
                error = "ProjFS (prjflt) service is not running";
                return false;
            }

            if (!IsNativeLibInstalled(tracer, new PhysicalFileSystem()))
            {
                error = "ProjFS native library (ProjectedFSLib.dll) is not installed. "
                    + "Ensure the Windows Projected File System optional feature is enabled. "
                    + "From an elevated PowerShell prompt, run: "
                    + "Enable-WindowsOptionalFeature -Online -FeatureName Client-ProjFS";
                return false;
            }

            if (!TryAttachToVolume(enlistmentRoot, tracer, out error))
            {
                return false;
            }

            return true;
        }

        public bool RegisterForOfflineIO()
        {
            return true;
        }

        public bool UnregisterForOfflineIO()
        {
            return true;
        }

        private static bool IsInboxAndEnabled()
        {
            ProcessResult getOptionalFeatureResult = GetProjFSOptionalFeatureStatus();
            return getOptionalFeatureResult.ExitCode == (int)ProjFSInboxStatus.Enabled;
        }

        private static bool TryGetWindowsBuildNumber(ITracer tracer, out uint windowsBuildNumber)
        {
            windowsBuildNumber = 0;
            try
            {
                windowsBuildNumber = Common.NativeMethods.GetWindowsBuildNumber();
                tracer.RelatedInfo($"{nameof(TryGetWindowsBuildNumber)}: Build number = {windowsBuildNumber}");
                return true;
            }
            catch (Win32Exception e)
            {
                tracer.RelatedWarning(CreateEventMetadata(e), $"{nameof(TryGetWindowsBuildNumber)}: Exception while trying to get Windows build number");
                return false;
            }
        }

        private static bool TryEnableProjFSOptionalFeature(ITracer tracer, PhysicalFileSystem fileSystem, out bool isProjFSFeatureAvailable)
        {
            EventMetadata metadata = CreateEventMetadata();
            ProcessResult getOptionalFeatureResult = GetProjFSOptionalFeatureStatus();

            isProjFSFeatureAvailable = true;
            bool projFSEnabled = false;
            switch (getOptionalFeatureResult.ExitCode)
            {
                case (int)ProjFSInboxStatus.NotInbox:
                    metadata.Add("getOptionalFeatureResult.Output", getOptionalFeatureResult.Output);
                    metadata.Add("getOptionalFeatureResult.Errors", getOptionalFeatureResult.Errors);
                    tracer.RelatedWarning(metadata, $"{nameof(TryEnableProjFSOptionalFeature)}: {OptionalFeatureName} optional feature is missing");

                    isProjFSFeatureAvailable = false;
                    break;

                case (int)ProjFSInboxStatus.Enabled:
                    tracer.RelatedEvent(
                        EventLevel.Informational,
                        $"{nameof(TryEnableProjFSOptionalFeature)}_ClientProjFSAlreadyEnabled",
                        metadata,
                        Keywords.Network);
                    projFSEnabled = true;
                    break;

                case (int)ProjFSInboxStatus.Disabled:
                    ProcessResult enableOptionalFeatureResult = CallPowershellCommand("try {Enable-WindowsOptionalFeature -Online -FeatureName " + OptionalFeatureName + " -NoRestart}catch{exit 1}");
                    metadata.Add("enableOptionalFeatureResult.Output", enableOptionalFeatureResult.Output.Trim().Replace("\r\n", ","));
                    metadata.Add("enableOptionalFeatureResult.Errors", enableOptionalFeatureResult.Errors);

                    if (enableOptionalFeatureResult.ExitCode == 0)
                    {
                        metadata.Add(TracingConstants.MessageKey.InfoMessage, "Enabled ProjFS optional feature");
                        tracer.RelatedEvent(EventLevel.Informational, $"{nameof(TryEnableProjFSOptionalFeature)}_ClientProjFSDisabled", metadata);
                        projFSEnabled = true;
                        break;
                    }

                    metadata.Add("enableOptionalFeatureResult.ExitCode", enableOptionalFeatureResult.ExitCode);
                    tracer.RelatedError(metadata, $"{nameof(TryEnableProjFSOptionalFeature)}: Failed to enable optional feature");
                    break;

                default:
                    metadata.Add("getOptionalFeatureResult.ExitCode", getOptionalFeatureResult.ExitCode);
                    metadata.Add("getOptionalFeatureResult.Output", getOptionalFeatureResult.Output);
                    metadata.Add("getOptionalFeatureResult.Errors", getOptionalFeatureResult.Errors);
                    tracer.RelatedError(metadata, $"{nameof(TryEnableProjFSOptionalFeature)}: Unexpected result");
                    isProjFSFeatureAvailable = false;
                    break;
            }

            if (projFSEnabled)
            {
                if (IsNativeLibInstalled(tracer, fileSystem))
                {
                    return true;
                }

                tracer.RelatedError($"{nameof(TryEnableProjFSOptionalFeature)}: {OptionalFeatureName} enabled, but native ProjFS library is not on path");
            }

            return false;
        }

        private static ProcessResult GetProjFSOptionalFeatureStatus()
        {
            try
            {
                return CallPowershellCommand(
                    "$var=(Get-WindowsOptionalFeature -Online -FeatureName " + OptionalFeatureName + ");  if($var -eq $null){exit " +
                    (int)ProjFSInboxStatus.NotInbox + "}else{if($var.State -eq 'Enabled'){exit " + (int)ProjFSInboxStatus.Enabled + "}else{exit " + (int)ProjFSInboxStatus.Disabled + "}}");
            }
            catch (PowershellNotFoundException e)
            {
                return new ProcessResult(string.Empty, e.Message, (int)ProjFSInboxStatus.Invalid);
            }
        }

        private static EventMetadata CreateEventMetadata(Exception e = null)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Area", EtwArea);
            if (e != null)
            {
                metadata.Add("Exception", e.ToString());
            }

            return metadata;
        }

        private static ProcessResult CallPowershellCommand(string command)
        {
            ProcessResult whereResult = ProcessHelper.Run("where.exe", "powershell.exe");

            if (whereResult.ExitCode != 0)
            {
                throw new PowershellNotFoundException();
            }

            return ProcessHelper.Run(whereResult.Output.Trim(), "-NonInteractive -NoProfile -Command \"& { " + command + " }\"");
        }

        // Using an Impl method allows TryPrepareFolderForCallbacks to catch any ProjFS dependency related exceptions
        // thrown in the process of calling this method.
        private bool TryPrepareFolderForCallbacksImpl(string folderPath, out string error)
        {
            error = string.Empty;
            Guid virtualizationInstanceGuid = Guid.NewGuid();
            HResult result = VirtualizationInstance.MarkDirectoryAsVirtualizationRoot(folderPath, virtualizationInstanceGuid);
            if (result != HResult.Ok)
            {
                error = "Failed to prepare \"" + folderPath + "\" for callbacks, error: " + result.ToString("F");
                return false;
            }

            return true;
        }

        private static class NativeMethods
        {
            [DllImport("fltlib.dll", CharSet = CharSet.Unicode)]
            public static extern uint FilterAttach(
                string filterName,
                string volumeName,
                string instanceName,
                uint createdInstanceNameLength = 0,
                string createdInstanceName = null);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetVolumePathName(
                string volumeName,
                StringBuilder volumePathName,
                uint bufferLength);
        }

        private class PowershellNotFoundException : Exception
        {
            public PowershellNotFoundException()
                : base("powershell.exe was not found")
            {
            }
        }
    }
}
