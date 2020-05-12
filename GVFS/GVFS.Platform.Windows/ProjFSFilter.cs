using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using Microsoft.Win32;
using Microsoft.Windows.ProjFS;
using System;
using System.ComponentModel;
using System.Diagnostics;
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
        private const string System32DriversRoot = @"%SystemRoot%\System32\drivers";

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

                return false;
            }

            return TryInstallProjFSViaINF(tracer, fileSystem);
        }

        public static bool IsNativeLibInstalled(ITracer tracer, PhysicalFileSystem fileSystem)
        {
            string system32Path = Path.Combine(Environment.SystemDirectory, ProjFSNativeLibFileName);
            bool existsInSystem32 = fileSystem.FileExists(system32Path);

            string gvfsAppDirectory = ProcessHelper.GetCurrentProcessLocation();
            string nonInboxNativeLibInstallPath;
            string packagedNativeLibPath;
            GetNativeLibPaths(gvfsAppDirectory, out packagedNativeLibPath, out nonInboxNativeLibInstallPath);
            bool existsInAppDirectory = fileSystem.FileExists(nonInboxNativeLibInstallPath);

            EventMetadata metadata = CreateEventMetadata();
            metadata.Add(nameof(system32Path), system32Path);
            metadata.Add(nameof(existsInSystem32), existsInSystem32);
            metadata.Add(nameof(gvfsAppDirectory), gvfsAppDirectory);
            metadata.Add(nameof(nonInboxNativeLibInstallPath), nonInboxNativeLibInstallPath);
            metadata.Add(nameof(packagedNativeLibPath), packagedNativeLibPath);
            metadata.Add(nameof(existsInAppDirectory), existsInAppDirectory);
            tracer.RelatedEvent(EventLevel.Informational, nameof(IsNativeLibInstalled), metadata);
            return existsInSystem32 || existsInAppDirectory;
        }

        public static bool TryCopyNativeLibIfDriverVersionsMatch(ITracer tracer, PhysicalFileSystem fileSystem, out string copyNativeDllError)
        {
            string system32NativeLibraryPath = Path.Combine(Environment.SystemDirectory, ProjFSNativeLibFileName);
            if (fileSystem.FileExists(system32NativeLibraryPath))
            {
                copyNativeDllError = $"{ProjFSNativeLibFileName} already exists at {system32NativeLibraryPath}";
                return false;
            }

            string gvfsProcessLocation = ProcessHelper.GetCurrentProcessLocation();
            string nonInboxNativeLibInstallPath;
            string packagedNativeLibPath;
            GetNativeLibPaths(gvfsProcessLocation, out packagedNativeLibPath, out nonInboxNativeLibInstallPath);
            if (fileSystem.FileExists(nonInboxNativeLibInstallPath))
            {
                copyNativeDllError = $"{ProjFSNativeLibFileName} already exists at {nonInboxNativeLibInstallPath}";
                return false;
            }

            if (!fileSystem.FileExists(packagedNativeLibPath))
            {
                copyNativeDllError = $"{packagedNativeLibPath} not found, no {ProjFSNativeLibFileName} available to copy";
                return false;
            }

            string packagedPrjfltDriverPath = Path.Combine(gvfsProcessLocation, "Filter", DriverFileName);
            if (!fileSystem.FileExists(packagedPrjfltDriverPath))
            {
                copyNativeDllError = $"{packagedPrjfltDriverPath} not found, unable to validate that packaged driver matches installed driver";
                return false;
            }

            string system32PrjfltDriverPath = Path.Combine(Environment.ExpandEnvironmentVariables(System32DriversRoot), DriverFileName);
            if (!fileSystem.FileExists(system32PrjfltDriverPath))
            {
                copyNativeDllError = $"{system32PrjfltDriverPath} not found, unable to validate that packaged driver matches installed driver";
                return false;
            }

            FileVersionInfo packagedDriverVersion;
            FileVersionInfo system32DriverVersion;
            try
            {
                packagedDriverVersion = fileSystem.GetVersionInfo(packagedPrjfltDriverPath);
                system32DriverVersion = fileSystem.GetVersionInfo(system32PrjfltDriverPath);
                if (!fileSystem.FileVersionsMatch(packagedDriverVersion, system32DriverVersion))
                {
                    copyNativeDllError = $"Packaged sys FileVersion '{packagedDriverVersion.FileVersion}' does not match System32 sys FileVersion '{system32DriverVersion.FileVersion}'";
                    return false;
                }

                if (!fileSystem.ProductVersionsMatch(packagedDriverVersion, system32DriverVersion))
                {
                    copyNativeDllError = $"Packaged sys ProductVersion '{packagedDriverVersion.ProductVersion}' does not match System32 sys ProductVersion '{system32DriverVersion.ProductVersion}'";
                    return false;
                }
            }
            catch (FileNotFoundException e)
            {
                EventMetadata metadata = CreateEventMetadata(e);
                tracer.RelatedWarning(
                    metadata,
                    $"{nameof(TryCopyNativeLibIfDriverVersionsMatch)}: Exception caught while comparing sys versions");
                copyNativeDllError = $"Exception caught while comparing sys versions: {e.Message}";
                return false;
            }

            EventMetadata driverVersionMetadata = CreateEventMetadata();
            driverVersionMetadata.Add($"{nameof(packagedDriverVersion)}.FileVersion", packagedDriverVersion.FileVersion.ToString());
            driverVersionMetadata.Add($"{nameof(system32DriverVersion)}.FileVersion", system32DriverVersion.FileVersion.ToString());
            driverVersionMetadata.Add($"{nameof(packagedDriverVersion)}.ProductVersion", packagedDriverVersion.ProductVersion.ToString());
            driverVersionMetadata.Add($"{nameof(system32DriverVersion)}.ProductVersion", system32DriverVersion.ProductVersion.ToString());
            tracer.RelatedInfo(driverVersionMetadata, $"{nameof(TryCopyNativeLibIfDriverVersionsMatch)}: Copying native library");

            if (!TryCopyNativeLibToNonInboxInstallLocation(tracer, fileSystem, gvfsProcessLocation))
            {
                copyNativeDllError = "Failed to copy native library";
                return false;
            }

            copyNativeDllError = null;
            return true;
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
            return
                IsServiceRunning(tracer) &&
                IsNativeLibInstalled(tracer, new PhysicalFileSystem()) &&
                TryAttach(enlistmentRoot, out error);
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
            if (!TryCopyNativeLibToNonInboxInstallLocation(tracer, fileSystem, gvfsAppDirectory))
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

        private static bool TryCopyNativeLibToNonInboxInstallLocation(ITracer tracer, PhysicalFileSystem fileSystem, string gvfsAppDirectory)
        {
            string packagedNativeLibPath;
            string nonInboxNativeLibInstallPath;
            GetNativeLibPaths(gvfsAppDirectory, out packagedNativeLibPath, out nonInboxNativeLibInstallPath);

            EventMetadata pathMetadata = CreateEventMetadata();
            pathMetadata.Add(nameof(gvfsAppDirectory), gvfsAppDirectory);
            pathMetadata.Add(nameof(packagedNativeLibPath), packagedNativeLibPath);
            pathMetadata.Add(nameof(nonInboxNativeLibInstallPath), nonInboxNativeLibInstallPath);

            if (fileSystem.FileExists(packagedNativeLibPath))
            {
                tracer.RelatedEvent(EventLevel.Informational, $"{nameof(TryCopyNativeLibToNonInboxInstallLocation)}_CopyingNativeLib", pathMetadata);

                try
                {
                    fileSystem.CopyFile(packagedNativeLibPath, nonInboxNativeLibInstallPath, overwrite: true);

                    try
                    {
                        fileSystem.FlushFileBuffers(nonInboxNativeLibInstallPath);
                    }
                    catch (Win32Exception e)
                    {
                        EventMetadata metadata = CreateEventMetadata(e);
                        metadata.Add(nameof(nonInboxNativeLibInstallPath), nonInboxNativeLibInstallPath);
                        metadata.Add(nameof(packagedNativeLibPath), packagedNativeLibPath);
                        tracer.RelatedWarning(metadata, $"{nameof(TryCopyNativeLibToNonInboxInstallLocation)}: Win32Exception while trying to flush file buffers", Keywords.Telemetry);
                    }
                }
                catch (UnauthorizedAccessException e)
                {
                    EventMetadata metadata = CreateEventMetadata(e);
                    tracer.RelatedError(metadata, $"{nameof(TryCopyNativeLibToNonInboxInstallLocation)}: UnauthorizedAccessException caught while trying to copy native lib");
                    return false;
                }
                catch (DirectoryNotFoundException e)
                {
                    EventMetadata metadata = CreateEventMetadata(e);
                    tracer.RelatedError(metadata, $"{nameof(TryCopyNativeLibToNonInboxInstallLocation)}: DirectoryNotFoundException caught while trying to copy native lib");
                    return false;
                }
                catch (FileNotFoundException e)
                {
                    EventMetadata metadata = CreateEventMetadata(e);
                    tracer.RelatedError(metadata, $"{nameof(TryCopyNativeLibToNonInboxInstallLocation)}: FileNotFoundException caught while trying to copy native lib");
                    return false;
                }
                catch (IOException e)
                {
                    EventMetadata metadata = CreateEventMetadata(e);
                    tracer.RelatedWarning(metadata, $"{nameof(TryCopyNativeLibToNonInboxInstallLocation)}: IOException caught while trying to copy native lib");

                    if (fileSystem.FileExists(nonInboxNativeLibInstallPath))
                    {
                        tracer.RelatedWarning(
                            CreateEventMetadata(),
                            "Could not copy native lib to app directory, but file already exists, continuing with install",
                            Keywords.Telemetry);
                    }
                    else
                    {
                        tracer.RelatedError($"{nameof(TryCopyNativeLibToNonInboxInstallLocation)}: Failed to copy native lib to app directory");
                        return false;
                    }
                }
            }
            else
            {
                tracer.RelatedError(pathMetadata, $"{nameof(TryCopyNativeLibToNonInboxInstallLocation)}: Native lib does not exist in install directory");
                return false;
            }

            return true;
        }

        private static void GetNativeLibPaths(string gvfsAppDirectory, out string packagedNativeLibPath, out string nonInboxNativeLibInstallPath)
        {
            packagedNativeLibPath = Path.Combine(gvfsAppDirectory, "ProjFS", ProjFSNativeLibFileName);
            nonInboxNativeLibInstallPath = Path.Combine(gvfsAppDirectory, ProjFSNativeLibFileName);
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
