using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Tests.EnlistmentPerFixture;
using GVFS.FunctionalTests.Tools;
using GVFS.FunctionalTests.Windows.Tools;
using GVFS.Tests.Should;
using Microsoft.Win32;
using NUnit.Framework;
using System;
using System.Runtime.InteropServices;
using System.ServiceProcess;

namespace GVFS.FunctionalTests.Windows.Tests
{
    [TestFixture]
    [NonParallelizable]
    [Category(Categories.ExtraCoverage)]
    public class ServiceTests : TestsWithEnlistmentPerFixture
    {
        private const string NativeLibPath = @"C:\Program Files\VFS for Git\ProjectedFSLib.dll";
        private const string PrjFltAutoLoggerKey = "SYSTEM\\CurrentControlSet\\Control\\WMI\\Autologger\\Microsoft-Windows-ProjFS-Filter-Log";
        private const string PrjFltAutoLoggerStartValue = "Start";

        private FileSystemRunner fileSystem;

        public ServiceTests()
        {
            this.fileSystem = new SystemIORunner();
        }

        [TestCase]
        public void MountAsksServiceToEnsurePrjFltServiceIsHealthy()
        {
            this.Enlistment.UnmountGVFS();
            StopPrjFlt();

            // Disable the ProjFS autologger
            RegistryHelper.GetValueFromRegistry(RegistryHive.LocalMachine, PrjFltAutoLoggerKey, PrjFltAutoLoggerStartValue).ShouldNotBeNull();
            RegistryHelper.TrySetDWordInRegistry(RegistryHive.LocalMachine, PrjFltAutoLoggerKey, PrjFltAutoLoggerStartValue, 0).ShouldBeTrue();

            this.Enlistment.MountGVFS();
            IsPrjFltRunning().ShouldBeTrue();

            // The service should have re-enabled the autologger
            Convert.ToInt32(RegistryHelper.GetValueFromRegistry(RegistryHive.LocalMachine, PrjFltAutoLoggerKey, PrjFltAutoLoggerStartValue)).ShouldEqual(1);
        }

        [TestCase]
        public void ServiceStartsPrjFltService()
        {
            this.Enlistment.UnmountGVFS();
            StopPrjFlt();
            GVFSServiceProcess.StopService();
            GVFSServiceProcess.StartService();

            ServiceController controller = new ServiceController("prjflt");
            controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
            controller.Status.ShouldEqual(ServiceControllerStatus.Running);

            this.Enlistment.MountGVFS();
        }

        private static bool IsPrjFltRunning()
        {
            ServiceController controller = new ServiceController("prjflt");
            return controller.Status.Equals(ServiceControllerStatus.Running);
        }

        private static void StopPrjFlt()
        {
            IsPrjFltRunning().ShouldBeTrue();

            ServiceController controller = new ServiceController("prjflt");
            controller.Stop();
            controller.WaitForStatus(ServiceControllerStatus.Stopped);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetVersionEx([In, Out] ref OSVersionInfo versionInfo);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OSVersionInfo
        {
            public uint OSVersionInfoSize;
            public uint MajorVersion;
            public uint MinorVersion;
            public uint BuildNumber;
            public uint PlatformId;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string CSDVersion;
        }
    }
}
