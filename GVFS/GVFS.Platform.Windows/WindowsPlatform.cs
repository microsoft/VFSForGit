using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using GVFS.Platform.Windows.DiskLayoutUpgrades;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Management.Automation;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;

namespace GVFS.Platform.Windows
{
    public partial class WindowsPlatform : GVFSPlatform
    {
        private const string WindowsVersionRegistryKey = "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion";
        private const string BuildLabRegistryValue = "BuildLab";
        private const string BuildLabExRegistryValue = "BuildLabEx";

        public WindowsPlatform() : base(underConstruction: new UnderConstructionFlags())
        {
        }

        public override IKernelDriver KernelDriver { get; } = new ProjFSFilter();
        public override IGitInstallation GitInstallation { get; } = new WindowsGitInstallation();
        public override IDiskLayoutUpgradeData DiskLayoutUpgrade { get; } = new WindowsDiskLayoutUpgradeData();
        public override IPlatformFileSystem FileSystem { get; } = new WindowsFileSystem();
        public override string Name { get => "Windows"; }
        public override GVFSPlatformConstants Constants { get; } = new WindowsPlatformConstants();

        public override string GVFSConfigPath
        {
            get
            {
                string servicePath = GVFSPlatform.Instance.GetDataRootForGVFSComponent(GVFSConstants.Service.ServiceName);
                string gvfsDirectory = Path.GetDirectoryName(servicePath);

                return Path.Combine(gvfsDirectory, LocalGVFSConfig.FileName);
            }
        }

        /// <summary>
        /// On Windows VFSForGit does not need to use system wide logs to track
        /// installer messages. VFSForGit is able to specifiy a custom installer
        /// log file as a commandline argument to the installer.
        /// </summary>
        public override bool SupportsSystemInstallLog
        {
            get
            {
                return false;
            }
        }

        public static string GetStringFromRegistry(string key, string valueName)
        {
            object value = GetValueFromRegistry(RegistryHive.LocalMachine, key, valueName);
            return value as string;
        }

        public static object GetValueFromRegistry(RegistryHive registryHive, string key, string valueName)
        {
            object value = GetValueFromRegistry(registryHive, key, valueName, RegistryView.Registry64);
            if (value == null)
            {
                value = GetValueFromRegistry(registryHive, key, valueName, RegistryView.Registry32);
            }

            return value;
        }

        public static bool TrySetDWordInRegistry(RegistryHive registryHive, string key, string valueName, uint value)
        {
            RegistryKey localKey = RegistryKey.OpenBaseKey(registryHive, RegistryView.Registry64);
            RegistryKey localKeySub = localKey.OpenSubKey(key, writable: true);

            if (localKeySub == null)
            {
                localKey = RegistryKey.OpenBaseKey(registryHive, RegistryView.Registry32);
                localKeySub = localKey.OpenSubKey(key, writable: true);
            }

            if (localKeySub == null)
            {
                return false;
            }

            localKeySub.SetValue(valueName, value, RegistryValueKind.DWord);
            return true;
        }

        public override string GetOSVersionInformation()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                string buildLabVersion = GetStringFromRegistry(WindowsVersionRegistryKey, BuildLabRegistryValue);
                sb.AppendFormat($"Windows BuildLab version {buildLabVersion}");
                sb.AppendLine();

                string buildLabExVersion = GetStringFromRegistry(WindowsVersionRegistryKey, BuildLabExRegistryValue);
                sb.AppendFormat($"Windows BuildLabEx version {buildLabExVersion}");
                sb.AppendLine();
            }
            catch (Exception e)
            {
                sb.AppendFormat($"Failed to record Windows version information. Exception: {e}");
            }

            return sb.ToString();
        }

        public override string GetDataRootForGVFS()
        {
            return WindowsPlatform.GetDataRootForGVFSImplementation();
        }

        public override string GetDataRootForGVFSComponent(string componentName)
        {
            return WindowsPlatform.GetDataRootForGVFSComponentImplementation(componentName);
        }

        public override string GetLogsRootForGVFS()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolderOption.Create),
                "GVFS");
        }

        public override string GetLogsDirectoryForGVFSComponent(string componentName)
        {
            return Path.Combine(this.GetLogsRootForGVFS(), componentName);
        }

        public override void StartBackgroundVFS4GProcess(ITracer tracer, string programName, string[] args)
        {
            string programArguments = string.Empty;
            try
            {
                programArguments = string.Join(" ", args.Select(arg => arg.Contains(' ') ? "\"" + arg + "\"" : arg));
                ProcessStartInfo processInfo = new ProcessStartInfo(programName, programArguments);
                processInfo.WindowStyle = ProcessWindowStyle.Hidden;

                Process executingProcess = new Process();
                executingProcess.StartInfo = processInfo;
                executingProcess.Start();
            }
            catch (Exception ex)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add(nameof(programName), programName);
                metadata.Add(nameof(programArguments), programArguments);
                metadata.Add("Exception", ex.ToString());
                tracer.RelatedError(metadata, "Failed to start background process.");
                throw;
            }
        }

        public override void PrepareProcessToRunInBackground()
        {
            // No additional work required
        }

        public override NamedPipeServerStream CreatePipeByName(string pipeName)
        {
            PipeSecurity security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.CreatorOwnerSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));

            NamedPipeServerStream pipe = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.WriteThrough | PipeOptions.Asynchronous,
                0, // default inBufferSize
                0, // default outBufferSize
                security,
                HandleInheritability.None);

            return pipe;
        }

        public override bool IsElevated()
        {
            return WindowsPlatform.IsElevatedImplementation();
        }

        public override bool IsProcessActive(int processId)
        {
            return WindowsPlatform.IsProcessActiveImplementation(processId, tryGetProcessById: true);
        }

        public override void IsServiceInstalledAndRunning(string name, out bool installed, out bool running)
        {
            ServiceController service = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName.Equals(name, StringComparison.Ordinal));

            installed = service != null;
            running = service != null ? service.Status == ServiceControllerStatus.Running : false;
        }

        public override string GetNamedPipeName(string enlistmentRoot)
        {
            return WindowsPlatform.GetNamedPipeNameImplementation(enlistmentRoot);
        }

        public override string GetGVFSServiceNamedPipeName(string serviceName)
        {
            return serviceName + ".pipe";
        }

        public override void ConfigureVisualStudio(string gitBinPath, ITracer tracer)
        {
            try
            {
                const string GitBinPathEnd = "\\cmd\\git.exe";
                string[] gitVSRegistryKeyNames =
                {
                    "HKEY_CURRENT_USER\\Software\\Microsoft\\VSCommon\\15.0\\TeamFoundation\\GitSourceControl",
                    "HKEY_CURRENT_USER\\Software\\Microsoft\\VSCommon\\16.0\\TeamFoundation\\GitSourceControl"
                };
                const string GitVSRegistryValueName = "GitPath";

                if (!gitBinPath.EndsWith(GitBinPathEnd))
                {
                    tracer.RelatedWarning(
                        "Unable to configure Visual Studio’s GitSourceControl regkey because invalid git.exe path found: " + gitBinPath,
                        Keywords.Telemetry);

                    return;
                }

                string regKeyValue = gitBinPath.Substring(0, gitBinPath.Length - GitBinPathEnd.Length);
                foreach (string registryKeyName in gitVSRegistryKeyNames)
                {
                    Registry.SetValue(registryKeyName, GitVSRegistryValueName, regKeyValue);
                }
            }
            catch (Exception ex)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Operation", nameof(this.ConfigureVisualStudio));
                metadata.Add("Exception", ex.ToString());
                tracer.RelatedWarning(metadata, "Error while trying to set Visual Studio’s GitSourceControl regkey");
            }
        }

        public override bool TryGetGVFSHooksVersion(out string hooksVersion, out string error)
        {
            error = null;
            hooksVersion = null;
            string hooksPath = ProcessHelper.GetProgramLocation(GVFSPlatform.Instance.Constants.ProgramLocaterCommand, GVFSPlatform.Instance.Constants.GVFSHooksExecutableName);
            if (hooksPath == null)
            {
                error = "Could not find " + GVFSPlatform.Instance.Constants.GVFSHooksExecutableName;
                return false;
            }

            FileVersionInfo hooksFileVersionInfo = FileVersionInfo.GetVersionInfo(Path.Combine(hooksPath, GVFSPlatform.Instance.Constants.GVFSHooksExecutableName));
            hooksVersion = hooksFileVersionInfo.ProductVersion;
            return true;
        }

        public override bool TryInstallGitCommandHooks(GVFSContext context, string executingDirectory, string hookName, string commandHookPath, out string errorMessage)
        {
            // The GitHooksLoader requires the following setup to invoke a hook:
            //      Copy GithooksLoader.exe to hook-name.exe
            //      Create a text file named hook-name.hooks that lists the applications to execute for the hook, one application per line

            string gitHooksloaderPath = Path.Combine(executingDirectory, GVFSConstants.DotGit.Hooks.LoaderExecutable);
            if (!HooksInstaller.TryHooksInstallationAction(
                () => HooksInstaller.CopyHook(context, gitHooksloaderPath, commandHookPath + GVFSPlatform.Instance.Constants.ExecutableExtension),
                out errorMessage))
            {
                errorMessage = "Failed to copy " + GVFSConstants.DotGit.Hooks.LoaderExecutable + " to " + commandHookPath + GVFSPlatform.Instance.Constants.ExecutableExtension + "\n" + errorMessage;
                return false;
            }

            if (!HooksInstaller.TryHooksInstallationAction(
                () => WindowsGitHooksInstaller.CreateHookCommandConfig(context, hookName, commandHookPath),
                out errorMessage))
            {
                errorMessage = "Failed to create " + commandHookPath + GVFSConstants.GitConfig.HooksExtension + "\n" + errorMessage;
                return false;
            }

            return true;
        }

        public override bool TryVerifyAuthenticodeSignature(string path, out string subject, out string issuer, out string error)
        {
            using (PowerShell powershell = PowerShell.Create())
            {
                powershell.AddScript($"Get-AuthenticodeSignature -FilePath {path}");

                Collection<PSObject> results = powershell.Invoke();
                if (powershell.HadErrors || results.Count <= 0)
                {
                    subject = null;
                    issuer = null;
                    error = $"Powershell Get-AuthenticodeSignature failed, could not verify authenticode for {path}.";
                    return false;
                }

                Signature signature = results[0].BaseObject as Signature;
                bool isValid = signature.Status == SignatureStatus.Valid;
                subject = signature.SignerCertificate.SubjectName.Name;
                issuer = signature.SignerCertificate.IssuerName.Name;
                error = isValid == false ? signature.StatusMessage : null;
                return isValid;
            }
        }

        public override string GetCurrentUser()
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return identity.User.Value;
        }

        public override string GetUserIdFromLoginSessionId(int sessionId, ITracer tracer)
        {
            using (CurrentUser currentUser = new CurrentUser(tracer, sessionId))
            {
                return currentUser.Identity.User.Value;
            }
        }

        public override string GetUpgradeLogDirectoryParentDirectory()
        {
            return this.GetLogsDirectoryForGVFSComponent(ProductUpgraderInfo.UpgradeDirectoryName);
        }

        public override string GetSystemInstallerLogPath()
        {
            return null;
        }

        public override string GetUpgradeHighestAvailableVersionDirectory()
        {
            return this.GetUpgradeProtectedDataDirectory();
        }

        public override string GetUpgradeProtectedDataDirectory()
        {
            return GetUpgradeProtectedDataDirectoryImplementation();
        }

        public override Dictionary<string, string> GetPhysicalDiskInfo(string path, bool sizeStatsOnly) => WindowsPhysicalDiskInfo.GetPhysicalDiskInfo(path, sizeStatsOnly);

        public override bool IsConsoleOutputRedirectedToFile()
        {
            return WindowsPlatform.IsConsoleOutputRedirectedToFileImplementation();
        }

        public override bool IsGitStatusCacheSupported()
        {
            return File.Exists(Path.Combine(GVFSPlatform.Instance.GetDataRootForGVFSComponent(GVFSConstants.Service.ServiceName), GVFSConstants.GitStatusCache.EnableGitStatusCacheTokenFile));
        }

        public override FileBasedLock CreateFileBasedLock(
            PhysicalFileSystem fileSystem,
            ITracer tracer,
            string lockPath)
        {
            return new WindowsFileBasedLock(fileSystem, tracer, lockPath);
        }

        public override ProductUpgraderPlatformStrategy CreateProductUpgraderPlatformInteractions(
            PhysicalFileSystem fileSystem,
            ITracer tracer)
        {
            return new WindowsProductUpgraderPlatformStrategy(fileSystem, tracer);
        }

        public override bool TryGetGVFSEnlistmentRoot(string directory, out string enlistmentRoot, out string errorMessage)
        {
            return WindowsPlatform.TryGetGVFSEnlistmentRootImplementation(directory, out enlistmentRoot, out errorMessage);
        }

        public override bool TryGetDefaultLocalCacheRoot(string enlistmentRoot, out string localCacheRoot, out string localCacheRootError)
        {
            string pathRoot;

            try
            {
                pathRoot = Path.GetPathRoot(enlistmentRoot);
            }
            catch (ArgumentException e)
            {
                localCacheRoot = null;
                localCacheRootError = $"Failed to determine the root of '{enlistmentRoot}'): {e.Message}";
                return false;
            }

            if (string.IsNullOrEmpty(pathRoot))
            {
                localCacheRoot = null;
                localCacheRootError = $"Failed to determine the root of '{enlistmentRoot}', path does not contain root directory information";
                return false;
            }

            try
            {
                localCacheRoot = Path.Combine(pathRoot, GVFSConstants.DefaultGVFSCacheFolderName);
                localCacheRootError = null;
                return true;
            }
            catch (ArgumentException e)
            {
                localCacheRoot = null;
                localCacheRootError = $"Failed to build local cache path using root directory '{pathRoot}'): {e.Message}";
                return false;
            }
        }

        public override bool TryKillProcessTree(int processId, out int exitCode, out string error)
        {
            ProcessResult result = ProcessHelper.Run("taskkill", $"/pid {processId} /f /t");
            error = result.Errors;
            exitCode = result.ExitCode;
            return result.ExitCode == 0;
        }

        public override bool TryCopyPanicLogs(string copyToDir, out string error)
        {
            error = null;
            return true;
        }

        private static object GetValueFromRegistry(RegistryHive registryHive, string key, string valueName, RegistryView view)
        {
            RegistryKey localKey = RegistryKey.OpenBaseKey(registryHive, view);
            RegistryKey localKeySub = localKey.OpenSubKey(key);

            object value = localKeySub == null ? null : localKeySub.GetValue(valueName);
            return value;
        }

        public class WindowsPlatformConstants : GVFSPlatformConstants
        {
            public override string ExecutableExtension
            {
                get { return ".exe"; }
            }

            public override string InstallerExtension
            {
                get { return ".exe"; }
            }

            public override bool SupportsUpgradeWhileRunning => false;

            public override string WorkingDirectoryBackingRootPath
            {
                get { return GVFSConstants.WorkingDirectoryRootName; }
            }

            public override string DotGVFSRoot
            {
                get { return WindowsPlatform.DotGVFSRoot; }
            }

            public override string GVFSBinDirectoryPath
            {
                get
                {
                    return Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                        this.GVFSBinDirectoryName);
                }
            }

            public override string GVFSBinDirectoryName
            {
                get { return "GVFS"; }
            }

            public override string GVFSExecutableName
            {
                get { return "GVFS" + this.ExecutableExtension; }
            }

            public override string ProgramLocaterCommand
            {
                get { return "where"; }
            }

            public override HashSet<string> UpgradeBlockingProcesses
            {
                get { return new HashSet<string>(GVFSPlatform.Instance.Constants.PathComparer) { "GVFS", "GVFS.Mount", "git", "ssh-agent", "wish", "bash" }; }
            }

            // Tests show that 250 is the max supported pipe name length
            public override int MaxPipePathLength => 250;

            public override string UpgradeInstallAdviceMessage
            {
                get { return $"When ready, run {this.UpgradeConfirmCommandMessage} from an elevated command prompt."; }
            }

            public override string UpgradeConfirmCommandMessage
            {
                get { return UpgradeConfirmMessage; }
            }

            public override string StartServiceCommandMessage
            {
                get { return $"`sc start GVFS.Service`";  }
            }

            public override string RunUpdateMessage
            {
                get { return $"Run {UpgradeConfirmMessage} from an elevated command prompt."; }
            }

            public override bool CaseSensitiveFileSystem => false;
        }
    }
}
