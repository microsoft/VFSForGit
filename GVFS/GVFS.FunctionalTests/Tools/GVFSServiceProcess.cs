using GVFS.Tests.Should;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;

namespace GVFS.FunctionalTests.Tools
{
    public static class GVFSServiceProcess
    {
        private static readonly string ServiceNameArgument = "--servicename=" + TestServiceName;
        private static Process consoleServiceProcess;

        public static string TestServiceName
        {
            get
            {
                return "Test.GVFS.Service";
            }
        }

        public static void InstallService()
        {
            if (GVFSTestConfig.IsDevMode)
            {
                StartServiceAsConsoleProcess();
            }
            else
            {
                InstallWindowsService();
            }
        }

        public static void UninstallService()
        {
            if (GVFSTestConfig.IsDevMode)
            {
                StopConsoleServiceProcess();
                CleanupServiceData();
            }
            else
            {
                UninstallWindowsService();
            }
        }

        public static void StartService()
        {
            if (GVFSTestConfig.IsDevMode)
            {
                StartServiceAsConsoleProcess();
            }
            else
            {
                StartWindowsService();
            }
        }

        public static void StopService()
        {
            if (GVFSTestConfig.IsDevMode)
            {
                StopConsoleServiceProcess();
            }
            else
            {
                StopWindowsService();
            }
        }

        private static void StartServiceAsConsoleProcess()
        {
            StopConsoleServiceProcess();

            string pathToService = GetPathToService();
            Console.WriteLine("Starting test service in console mode: " + pathToService);

            ProcessStartInfo startInfo = new ProcessStartInfo(pathToService);
            startInfo.Arguments = $"--console {ServiceNameArgument}";
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;

            consoleServiceProcess = Process.Start(startInfo);
            consoleServiceProcess.ShouldNotBeNull("Failed to start test service process");

            // Consume output asynchronously to prevent buffer deadlock
            consoleServiceProcess.BeginOutputReadLine();
            consoleServiceProcess.BeginErrorReadLine();

            // Wait for the service to start listening on its named pipe
            string pipeName = TestServiceName + ".pipe";
            int retries = 50;
            while (retries-- > 0)
            {
                if (consoleServiceProcess.HasExited)
                {
                    throw new InvalidOperationException(
                        $"Test service process exited with code {consoleServiceProcess.ExitCode} before becoming ready");
                }

                if (File.Exists(@"\\.\pipe\" + pipeName))
                {
                    Console.WriteLine("Test service is ready (pipe: " + pipeName + ")");
                    return;
                }

                Thread.Sleep(200);
            }

            throw new System.TimeoutException("Timed out waiting for test service pipe: " + pipeName);
        }

        private static void StopConsoleServiceProcess()
        {
            if (consoleServiceProcess != null && !consoleServiceProcess.HasExited)
            {
                try
                {
                    Console.WriteLine("Stopping test service console process (PID: " + consoleServiceProcess.Id + ")");
                    consoleServiceProcess.Kill();
                    consoleServiceProcess.WaitForExit(5000);
                }
                catch (InvalidOperationException)
                {
                    // Process already exited
                }

                consoleServiceProcess = null;
            }
        }

        private static void CleanupServiceData()
        {
            string commonAppDataRoot = Environment.GetEnvironmentVariable("GVFS_COMMON_APPDATA_ROOT");
            string serviceData;
            if (!string.IsNullOrEmpty(commonAppDataRoot))
            {
                serviceData = Path.Combine(commonAppDataRoot, TestServiceName);
            }
            else
            {
                serviceData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "GVFS",
                    TestServiceName);
            }

            DirectoryInfo serviceDataDir = new DirectoryInfo(serviceData);
            if (serviceDataDir.Exists)
            {
                serviceDataDir.Delete(true);
            }
        }

        private static void InstallWindowsService()
        {
            Console.WriteLine("Installing " + TestServiceName);

            UninstallWindowsService();

            // Wait for delete to complete. If the services control panel is open, this will never complete.
            while (RunScCommand("query", TestServiceName).ExitCode == 0)
            {
                Thread.Sleep(1000);
            }

            // Install service
            string pathToService = GetPathToService();
            Console.WriteLine("Using service executable: " + pathToService);

            File.Exists(pathToService).ShouldBeTrue($"{pathToService} does not exist");

            string createServiceArguments = string.Format(
                "{0} binPath= \"{1}\"",
                TestServiceName,
                pathToService);

            ProcessResult result = RunScCommand("create", createServiceArguments);
            result.ExitCode.ShouldEqual(0, "Failure while running sc create " + createServiceArguments + "\r\n" + result.Output);

            StartWindowsService();
        }

        private static void UninstallWindowsService()
        {
            StopWindowsService();

            RunScCommand("delete", TestServiceName);

            // Make sure to delete any test service data state
            string serviceData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GVFS", TestServiceName);
            DirectoryInfo serviceDataDir = new DirectoryInfo(serviceData);
            if (serviceDataDir.Exists)
            {
                serviceDataDir.Delete(true);
            }
        }

        private static void StartWindowsService()
        {
            ServiceController testService = ServiceController.GetServices().SingleOrDefault(service => service.ServiceName == TestServiceName);
            testService.ShouldNotBeNull($"{TestServiceName} does not exist as a service");

            using (ServiceController controller = new ServiceController(TestServiceName))
            {
                controller.Start(new[] { ServiceNameArgument });
                controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                controller.Status.ShouldEqual(ServiceControllerStatus.Running);
            }
        }

        private static void StopWindowsService()
        {
            try
            {
                ServiceController testService = ServiceController.GetServices().SingleOrDefault(service => service.ServiceName == TestServiceName);
                if (testService != null)
                {
                    if (testService.Status == ServiceControllerStatus.Running)
                    {
                        testService.Stop();
                    }

                    testService.WaitForStatus(ServiceControllerStatus.Stopped);
                }
            }
            catch (InvalidOperationException)
            {
                return;
            }
        }

        private static ProcessResult RunScCommand(string command, string parameters)
        {
            ProcessStartInfo processInfo = new ProcessStartInfo("sc");
            processInfo.WindowStyle = ProcessWindowStyle.Hidden;
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardOutput = true;

            processInfo.Arguments = command + " " + parameters;

            return ProcessHelper.Run(processInfo);
        }

        private static string GetPathToService()
        {
            File.Exists(Properties.Settings.Default.PathToGVFSService).ShouldBeTrue("Failed to locate GVFS.Service.exe");
            return Properties.Settings.Default.PathToGVFSService;
        }
    }
}
