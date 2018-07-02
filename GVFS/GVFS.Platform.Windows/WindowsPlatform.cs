using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using GVFS.Platform.Windows.DiskLayoutUpgrades;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace GVFS.Platform.Windows
{
    public partial class WindowsPlatform : GVFSPlatform
    {
        private const string WindowsVersionRegistryKey = "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion";
        private const string BuildLabRegistryValue = "BuildLab";
        private const string BuildLabExRegistryValue = "BuildLabEx";

        public override IKernelDriver KernelDriver { get; } = new ProjFSFilter();
        public override IGitInstallation GitInstallation { get; } = new WindowsGitInstallation();
        public override IDiskLayoutUpgradeData DiskLayoutUpgrade { get; } = new WindowsDiskLayoutUpgradeData();
        public override IPlatformFileSystem FileSystem { get; } = new WindowsFileSystem();

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

        public override InProcEventListener CreateTelemetryListenerIfEnabled(string providerName)
        {
            return ETWTelemetryEventListener.CreateTelemetryListenerIfEnabled(
                this.GitInstallation.GetInstalledGitBinPath(),
                providerName);
        }

        public override void InitializeEnlistmentACLs(string enlistmentPath)
        {
            // The following permissions are typically present on deskop and missing on Server
            //                  
            //   ACCESS_ALLOWED_ACE_TYPE: NT AUTHORITY\Authenticated Users
            //          [OBJECT_INHERIT_ACE]
            //          [CONTAINER_INHERIT_ACE]
            //          [INHERIT_ONLY_ACE]
            //        DELETE
            //        GENERIC_EXECUTE
            //        GENERIC_WRITE
            //        GENERIC_READ
            DirectorySecurity rootSecurity = Directory.GetAccessControl(enlistmentPath);
            AccessRule authenticatedUsersAccessRule = rootSecurity.AccessRuleFactory(
                new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                unchecked((int)(NativeMethods.FileAccess.DELETE | NativeMethods.FileAccess.GENERIC_EXECUTE | NativeMethods.FileAccess.GENERIC_WRITE | NativeMethods.FileAccess.GENERIC_READ)),
                true,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow);

            // The return type of the AccessRuleFactory method is the base class, AccessRule, but the return value can be cast safely to the derived class.
            // https://msdn.microsoft.com/en-us/library/system.security.accesscontrol.filesystemsecurity.accessrulefactory(v=vs.110).aspx
            rootSecurity.AddAccessRule((FileSystemAccessRule)authenticatedUsersAccessRule);
            Directory.SetAccessControl(enlistmentPath, rootSecurity);
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

        public override void StartBackgroundProcess(string programName, string[] args)
        {
            ProcessStartInfo processInfo = new ProcessStartInfo(
                programName, 
                string.Join(" ", args.Select(arg => arg.Contains(' ') ? "\"" + arg + "\"" : arg)));
            processInfo.WindowStyle = ProcessWindowStyle.Hidden;

            Process executingProcess = new Process();
            executingProcess.StartInfo = processInfo;

            executingProcess.Start();
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

        public override void ConfigureVisualStudio(string gitBinPath, ITracer tracer)
        {
            const string GitBinPathEnd = "\\cmd\\git.exe";
            const string GitVSRegistryKeyName = "HKEY_CURRENT_USER\\Software\\Microsoft\\VSCommon\\15.0\\TeamFoundation\\GitSourceControl";
            const string GitVSRegistryValueName = "GitPath";

            if (!gitBinPath.EndsWith(GitBinPathEnd))
            {
                tracer.RelatedWarning(
                    "Unable to configure Visual Studio’s GitSourceControl regkey because invalid git.exe path found: " + gitBinPath,
                    Keywords.Telemetry);

                return;
            }

            string regKeyValue = gitBinPath.Substring(0, gitBinPath.Length - GitBinPathEnd.Length);
            Registry.SetValue(GitVSRegistryKeyName, GitVSRegistryValueName, regKeyValue);
        }

        public override bool TryGetGVFSHooksPathAndVersion(out string hooksPath, out string hooksVersion, out string error)
        {
            error = null;
            hooksVersion = null;
            hooksPath = ProcessHelper.WhereDirectory(GVFSConstants.GVFSHooksExecutableName);
            if (hooksPath == null)
            {
                error = "Could not find " + GVFSConstants.GVFSHooksExecutableName;
                return false;
            }

            FileVersionInfo hooksFileVersionInfo = FileVersionInfo.GetVersionInfo(hooksPath + "\\" + GVFSConstants.GVFSHooksExecutableName);
            hooksVersion = hooksFileVersionInfo.ProductVersion;
            return true;
        }

        public override string GetCurrentUser()
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return identity.User.Value;
        }

        public override Dictionary<string, string> GetPhysicalDiskInfo(string path) => WindowsPhysicalDiskInfo.GetPhysicalDiskInfo(path);

        public override bool IsConsoleOutputRedirectedToFile()
        {
            return ConsoleHelper.IsConsoleOutputRedirectedToFile();
        }

        public override bool TryGetGVFSEnlistmentRoot(string directory, out string enlistmentRoot, out string errorMessage)
        {
            return WindowsPlatform.TryGetGVFSEnlistmentRootImplementation(directory, out enlistmentRoot, out errorMessage);
        }

        private static object GetValueFromRegistry(RegistryHive registryHive, string key, string valueName, RegistryView view)
        {
            RegistryKey localKey = RegistryKey.OpenBaseKey(registryHive, view);
            RegistryKey localKeySub = localKey.OpenSubKey(key);

            object value = localKeySub == null ? null : localKeySub.GetValue(valueName);
            return value;
        }
    }
}
