using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using Microsoft.Win32;
using ProjFS;
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

        // From "Autologger" section of prjflt.inf
        private const string FilterLoggerGuid = "ee4206ff-4a4d-452f-be56-6bd0ed272b44";
        private const string FilterLoggerSessionName = "Microsoft-Windows-ProjFS-Filter-Log";

        private const string ProjFSNativeLibFileName = "ProjectedFSLib.dll";
        private const string ProjFSManagedLibFileName = "ProjectedFSLib.Managed.dll";

        private const uint OkResult = 0;
        private const uint NameCollisionErrorResult = 0x801F0012;

        private enum ProjFSInboxStatus
        {
            Invalid,
            NotInbox = 2,
            Enabled = 3,
            Disabled = 4,
        }

        public bool EnumerationExpandsDirectories { get; } = false;

        public string DriverLogFolderName { get; } = ProjFSFilter.ServiceName;

        public static bool TryAttach(ITracer tracer, string enlistmentRoot, out string errorMessage)
        {
            errorMessage = null;
            try
            {
                StringBuilder volumePathName = new StringBuilder(GVFSConstants.MaxPath);
                if (!NativeMethods.GetVolumePathName(enlistmentRoot, volumePathName, GVFSConstants.MaxPath))
                {
                    errorMessage = "Could not get volume path name";
                    tracer.RelatedError($"{nameof(TryAttach)}:{errorMessage}");
                    return false;
                }

                uint result = NativeMethods.FilterAttach(DriverName, volumePathName.ToString(), null);
                if (result != OkResult && result != NameCollisionErrorResult)
                {
                    errorMessage = string.Format("Attaching the filter driver resulted in: {0}", result);
                    tracer.RelatedError(errorMessage);
                    return false;
                }
            }
            catch (Exception e)
            {
                errorMessage = string.Format("Attaching the filter driver resulted in: {0}", e.Message);
                tracer.RelatedError(errorMessage);
                return false;
            }

            return true;
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
            out bool isServiceInstalled, 
            out bool isDriverFileInstalled,
            out bool isNativeLibInstalled)
        {
            bool isRunning = false;
            isServiceInstalled = false;
            isDriverFileInstalled = fileSystem.FileExists(Path.Combine(Environment.SystemDirectory, "drivers", DriverFileName));
            isNativeLibInstalled = IsNativeLibInstalled(tracer, fileSystem);

            try
            {
                ServiceController controller = new ServiceController(DriverName);
                isRunning = controller.Status.Equals(ServiceControllerStatus.Running);
                isServiceInstalled = true;
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
            if (!TryGetIsInboxProjFSFinalAPI(tracer, out windowsBuildNumber, out isInboxProjFSFinalAPI))
            {
                return false;
            }

            if (isInboxProjFSFinalAPI)
            {
                if (TryEnableProjFSOptionalFeature(tracer, fileSystem, out isProjFSFeatureAvailable))
                {
                    return true;
                }

                if (!isProjFSFeatureAvailable)
                {
                    // If enabling ProjFS failed because we were unable to find the optional feature, fallback
                    // on installing ProjFS via the INF
                    return TryInstallProjFSViaINF(tracer, fileSystem);
                }

                return false;
            }

            return TryInstallProjFSViaINF(tracer, fileSystem);
        }

        public static bool IsNativeLibInstalled(ITracer tracer, PhysicalFileSystem fileSystem)
        {
            string system32Path = Path.Combine(Environment.SystemDirectory, ProjFSNativeLibFileName);
            bool existsInSystem32 = fileSystem.FileExists(system32Path);

            string gvfsAppDirectory = ProcessHelper.GetCurrentProcessLocation();
            string appFilePath;
            string installFilePath;
            GetNativeLibPaths(gvfsAppDirectory, out installFilePath, out appFilePath);
            bool existsInAppDirectory = fileSystem.FileExists(appFilePath);

            EventMetadata metadata = CreateEventMetadata();
            metadata.Add(nameof(system32Path), system32Path);
            metadata.Add(nameof(existsInSystem32), existsInSystem32);
            metadata.Add(nameof(gvfsAppDirectory), gvfsAppDirectory);
            metadata.Add(nameof(appFilePath), appFilePath);
            metadata.Add(nameof(installFilePath), installFilePath);
            metadata.Add(nameof(existsInAppDirectory), existsInAppDirectory);
            tracer.RelatedEvent(EventLevel.Informational, nameof(IsNativeLibInstalled), metadata);
            return existsInSystem32 || existsInAppDirectory;
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
            string requiredFormat = "NTFS";
            if (rootDriveInfo == null)
            {
                warning = $"Unable to ensure that '{normalizedEnlistmentRootPath}' is an {requiredFormat} volume.";
            }
            else if (!string.Equals(rootDriveInfo.DriveFormat, requiredFormat, StringComparison.OrdinalIgnoreCase))
            {
                error = $"Error: Currently only {requiredFormat} volumes are supported.  Ensure repo is located into an {requiredFormat} volume.";
                return false;
            }

            if (Common.NativeMethods.IsFeatureSupportedByVolume(
                Directory.GetDirectoryRoot(normalizedEnlistmentRootPath),
                Common.NativeMethods.FileSystemFlags.FILE_RETURNS_CLEANUP_RESULT_INFO))
            {
                return true;
            }

            error = "File system does not support features required by GVFS. Confirm that Windows version is at or beyond that required by GVFS";
            return false;
        }

        public string FlushDriverLogs()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                string logfileName;
                uint result = Common.NativeMethods.FlushTraceLogger(FilterLoggerSessionName, FilterLoggerGuid, out logfileName);
                if (result != 0)
                {
                    sb.AppendFormat($"Failed to flush {ProjFSFilter.ServiceName} log buffers {result}");
                }
            }
            catch (Exception e)
            {
                sb.AppendFormat($"Failed to flush {ProjFSFilter.ServiceName} log buffers, exception: {e.ToString()}");
            }

            return sb.ToString();
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

                if (e.FileName.Equals(ProjFSManagedLibFileName, StringComparison.OrdinalIgnoreCase))
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
        public bool IsReady(JsonTracer tracer, string enlistmentRoot, out string error)
        {
            error = string.Empty;
            return
                IsServiceRunning(tracer) &&
                IsNativeLibInstalled(tracer, new PhysicalFileSystem()) &&
                TryAttach(tracer, enlistmentRoot, out error);
        }

        private static bool IsInboxAndEnabled()
        {
            ProcessResult getOptionalFeatureResult = GetProjFSOptionalFeatureStatus();
            return getOptionalFeatureResult.ExitCode == (int)ProjFSInboxStatus.Enabled;
        }

        private static bool TryGetIsInboxProjFSFinalAPI(ITracer tracer, out uint windowsBuildNumber, out bool isProjFSInbox)
        {
            isProjFSInbox = false;
            windowsBuildNumber = 0;
            try
            {
                windowsBuildNumber = Common.NativeMethods.GetWindowsBuildNumber();
                tracer.RelatedInfo($"{nameof(TryGetIsInboxProjFSFinalAPI)}: Build number = {windowsBuildNumber}");
            }
            catch (Win32Exception e)
            {                
                tracer.RelatedError(CreateEventMetadata(e), $"{nameof(TryGetIsInboxProjFSFinalAPI)}: Exception while trying to get Windows build number");
                return false;
            }

            const uint MinRS4inboxVersion = 17121;
            const uint FirstRS5Version = 17600;
            const uint MinRS5inboxVersion = 17626;
            isProjFSInbox = !(windowsBuildNumber < MinRS4inboxVersion || (windowsBuildNumber >= FirstRS5Version && windowsBuildNumber < MinRS5inboxVersion));
            return true;
        }

        private static bool TryInstallProjFSViaINF(ITracer tracer, PhysicalFileSystem fileSystem)
        {
            string gvfsAppDirectory = ProcessHelper.GetCurrentProcessLocation();
            if (!TryCopyNativeLibToAppDirectory(tracer, fileSystem, gvfsAppDirectory))
            {
                return false;
            }            

            ProcessResult result = ProcessHelper.Run("RUNDLL32.EXE", $"SETUPAPI.DLL,InstallHinfSection DefaultInstall 128 {gvfsAppDirectory}\\Filter\\prjflt.inf");
            if (result.ExitCode == 0)
            {
                tracer.RelatedInfo($"{nameof(TryInstallProjFSViaINF)}: Installed PrjFlt via INF");
                return true;
            }
            else
            {
                EventMetadata metadata = CreateEventMetadata();
                metadata.Add("resultExitCode", result.ExitCode);
                metadata.Add("resultOutput", result.Output);
                tracer.RelatedError(metadata, $"{nameof(TryInstallProjFSViaINF)}: RUNDLL32.EXE failed to install PrjFlt");
            }

            return false;
        }

        private static bool TryCopyNativeLibToAppDirectory(ITracer tracer, PhysicalFileSystem fileSystem, string gvfsAppDirectory)
        {
            string installFilePath;
            string appFilePath;
            GetNativeLibPaths(gvfsAppDirectory, out installFilePath, out appFilePath);

            EventMetadata pathMetadata = CreateEventMetadata();
            pathMetadata.Add(nameof(gvfsAppDirectory), gvfsAppDirectory);
            pathMetadata.Add(nameof(installFilePath), installFilePath);
            pathMetadata.Add(nameof(appFilePath), appFilePath);

            if (fileSystem.FileExists(installFilePath))
            {
                tracer.RelatedEvent(EventLevel.Informational, $"{nameof(TryCopyNativeLibToAppDirectory)}_CopyingNativeLib", pathMetadata);

                try
                {
                    fileSystem.CopyFile(installFilePath, appFilePath, overwrite: true);

                    try
                    {
                        Common.NativeMethods.FlushFileBuffers(appFilePath);
                    }
                    catch (Win32Exception e)
                    {
                        EventMetadata metadata = CreateEventMetadata(e);
                        metadata.Add(nameof(appFilePath), appFilePath);
                        metadata.Add(nameof(installFilePath), installFilePath);
                        tracer.RelatedWarning(metadata, $"{nameof(TryCopyNativeLibToAppDirectory)}: Win32Exception while trying to flush file buffers", Keywords.Telemetry);
                    }
                }
                catch (UnauthorizedAccessException e)
                {
                    EventMetadata metadata = CreateEventMetadata(e);
                    tracer.RelatedError(metadata, $"{nameof(TryCopyNativeLibToAppDirectory)}: UnauthorizedAccessException caught while trying to copy native lib");
                    return false;
                }
                catch (DirectoryNotFoundException e)
                {
                    EventMetadata metadata = CreateEventMetadata(e);
                    tracer.RelatedError(metadata, $"{nameof(TryCopyNativeLibToAppDirectory)}: DirectoryNotFoundException caught while trying to copy native lib");
                    return false;
                }
                catch (FileNotFoundException e)
                {
                    EventMetadata metadata = CreateEventMetadata(e);
                    tracer.RelatedError(metadata, $"{nameof(TryCopyNativeLibToAppDirectory)}: FileNotFoundException caught while trying to copy native lib");
                    return false;
                }
                catch (IOException e)
                {
                    EventMetadata metadata = CreateEventMetadata(e);
                    tracer.RelatedWarning(metadata, $"{nameof(TryCopyNativeLibToAppDirectory)}: IOException caught while trying to copy native lib");

                    if (fileSystem.FileExists(appFilePath))
                    {
                        tracer.RelatedWarning(
                            CreateEventMetadata(),
                            "Could not copy native lib to app directory, but file already exists, continuing with install",
                            Keywords.Telemetry);
                    }
                    else
                    {
                        tracer.RelatedError($"{nameof(TryCopyNativeLibToAppDirectory)}: Failed to copy native lib to app directory");
                        return false;
                    }
                }
            }
            else
            {
                tracer.RelatedError(pathMetadata, $"{nameof(TryCopyNativeLibToAppDirectory)}: Native lib does not exist in install directory");
                return false;
            }

            return true;
        }

        private static void GetNativeLibPaths(string gvfsAppDirectory, out string installFilePath, out string appFilePath)
        {
            installFilePath = Path.Combine(gvfsAppDirectory, "ProjFS", ProjFSNativeLibFileName);
            appFilePath = Path.Combine(gvfsAppDirectory, ProjFSNativeLibFileName);            
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
            return CallPowershellCommand(
                "$var=(Get-WindowsOptionalFeature -Online -FeatureName " + OptionalFeatureName + ");  if($var -eq $null){exit " +
                (int)ProjFSInboxStatus.NotInbox + "}else{if($var.State -eq 'Enabled'){exit " + (int)ProjFSInboxStatus.Enabled + "}else{exit " + (int)ProjFSInboxStatus.Disabled + "}}");
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
            return ProcessHelper.Run("powershell.exe", "-NonInteractive -NoProfile -Command \"& { " + command + " }\"");
        }

        // Using an Impl method allows TryPrepareFolderForCallbacks to catch any ProjFS dependency related exceptions
        // thrown in the process of calling this method.
        private bool TryPrepareFolderForCallbacksImpl(string folderPath, out string error)
        {
            error = string.Empty;
            Guid virtualizationInstanceGuid = Guid.NewGuid();
            HResult result = VirtualizationInstance.ConvertDirectoryToVirtualizationRoot(virtualizationInstanceGuid, folderPath);
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
    }
}
