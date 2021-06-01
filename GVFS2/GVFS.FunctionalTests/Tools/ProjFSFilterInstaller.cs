using GVFS.Tests.Should;
using System;
using System.IO;

namespace GVFS.FunctionalTests.Tools
{
    public class ProjFSFilterInstaller
    {
        private const string GVFSServiceName = "GVFS.Service";
        private const string ProjFSServiceName = "prjflt";
        private const string OptionalFeatureName = "Client-ProjFS";
        private const string GVFSInstallPath = @"C:\Program Files\GVFS";
        private const string NativeProjFSLibInstallLocation = GVFSInstallPath + @"\ProjFS\ProjectedFSLib.dll";

        private const string PrjfltInfName = "prjflt.inf";
        private const string PrjfltInfInstallFolder = GVFSInstallPath + @"\Filter";

        private const string PrjfltSysName = "prjflt.sys";
        private const string System32DriversPath = @"C:\Windows\System32\drivers";

        public static void ReplaceInboxProjFS()
        {
            if (IsInboxProjFSEnabled())
            {
                StopService(GVFSServiceName);
                StopService(ProjFSServiceName);
                DisableAndRemoveInboxProjFS();
                InstallProjFSViaINF();
                ValidateProjFSInstalled();
                StartService(ProjFSServiceName);
                StartService(GVFSServiceName);
            }
            else
            {
                ValidateProjFSInstalled();
            }
        }

        private static ProcessResult CallPowershellCommand(string command)
        {
            return ProcessHelper.Run("powershell.exe", "-NonInteractive -NoProfile -Command \"& { " + command + " }\"");
        }

        private static bool IsInboxProjFSEnabled()
        {
            const int ProjFSNotAnOptionalFeature = 2;
            const int ProjFSEnabled = 3;
            const int ProjFSDisabled = 4;

            ProcessResult getOptionalFeatureResult = CallPowershellCommand(
                "$var=(Get-WindowsOptionalFeature -Online -FeatureName " + OptionalFeatureName + ");  if($var -eq $null){exit " +
                ProjFSNotAnOptionalFeature + "}else{if($var.State -eq 'Enabled'){exit " + ProjFSEnabled + "}else{exit " + ProjFSDisabled + "}}");

            return getOptionalFeatureResult.ExitCode == ProjFSEnabled;
        }

        private static void StartService(string serviceName)
        {
            ProcessResult result = ProcessHelper.Run("sc.exe", $"start {serviceName}");
            Console.WriteLine($"sc start {serviceName} Output: {result.Output}");
            Console.WriteLine($"sc start {serviceName} Errors: {result.Errors}");
            result.ExitCode.ShouldEqual(0, $"Failed to start {serviceName}");
        }

        private static void StopService(string serviceName)
        {
            ProcessResult result = ProcessHelper.Run("sc.exe", $"stop {serviceName}");

            // 1060 -> The specified service does not exist as an installed service
            // 1062 -> The service has not been started
            bool stopSucceeded = result.ExitCode == 0 || result.ExitCode == 1060 || result.ExitCode == 1062;
            Console.WriteLine($"sc stop {serviceName} Output: {result.Output}");
            Console.WriteLine($"sc stop {serviceName} Errors: {result.Errors}");
            stopSucceeded.ShouldBeTrue($"Failed to stop {serviceName}");
        }

        private static void DisableAndRemoveInboxProjFS()
        {
            ProcessResult disableFeatureResult = CallPowershellCommand("Disable-WindowsOptionalFeature -Online -FeatureName " + OptionalFeatureName + " -Remove");
            Console.WriteLine($"Disable ProjfS Output: {disableFeatureResult.Output}");
            Console.WriteLine($"Disable ProjfS Errors: {disableFeatureResult.Errors}");
            disableFeatureResult.ExitCode.ShouldEqual(0, "Error when disabling ProjFS");
        }

        private static void InstallProjFSViaINF()
        {
            File.Exists(NativeProjFSLibInstallLocation).ShouldBeTrue($"{NativeProjFSLibInstallLocation} missing");
            File.Copy(NativeProjFSLibInstallLocation, GVFSInstallPath + @"\ProjectedFSLib.dll", overwrite: true);

            string prjfltInfInstallLocation = Path.Combine(PrjfltInfInstallFolder, PrjfltInfName);
            File.Exists(prjfltInfInstallLocation).ShouldBeTrue($"{prjfltInfInstallLocation} missing");
            ProcessResult result = ProcessHelper.Run("RUNDLL32.EXE", $"SETUPAPI.DLL,InstallHinfSection DefaultInstall 128 {prjfltInfInstallLocation}");
            result.ExitCode.ShouldEqual(0, "Failed to install ProjFS via INF");
        }

        private static void ValidateProjFSInstalled()
        {
            string installPathPrjflt = Path.Combine(PrjfltInfInstallFolder, PrjfltSysName);
            string system32Prjflt = Path.Combine(System32DriversPath, PrjfltSysName);
            ProcessResult result = ProcessHelper.Run("fc.exe", $"/b \"{installPathPrjflt}\" \"{system32Prjflt}\"");
            result.ExitCode.ShouldEqual(0, $"fc failed to validate prjflt.sys");
            result.Output.ShouldContain("no differences encountered");
        }
    }
}
