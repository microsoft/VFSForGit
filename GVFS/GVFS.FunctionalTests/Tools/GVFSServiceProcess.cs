using GVFS.Tests.Should;
using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;

namespace GVFS.FunctionalTests.Tools
{
    public static class GVFSServiceProcess
    {
        private static readonly Dictionary<string, Process> ConsoleServiceProcesses =
            new Dictionary<string, Process>(System.StringComparer.OrdinalIgnoreCase);

        public static string TestServiceName
        {
            get
            {
                string name = Environment.GetEnvironmentVariable("GVFS_TEST_SERVICE_NAME");
                return string.IsNullOrWhiteSpace(name) ? "Test.GVFS.Service" : name;
            }
        }

        public static void InstallService()
        {
            InstallService(TestServiceName);
        }

        public static void InstallService(string serviceName)
        {
            if (GVFSTestConfig.IsDevMode)
            {
                StartServiceAsConsoleProcess(serviceName);
            }
            else
            {
                InstallWindowsService(serviceName);
            }
        }

        public static void UninstallService()
        {
            UninstallService(TestServiceName);
        }

        public static void UninstallService(string serviceName)
        {
            if (GVFSTestConfig.IsDevMode)
            {
                StopConsoleServiceProcess(serviceName);
                CleanupServiceData(serviceName);
            }
            else
            {
                UninstallWindowsService(serviceName);
            }
        }

        public static void StartService()
        {
            StartService(TestServiceName);
        }

        public static void StartService(string serviceName)
        {
            if (GVFSTestConfig.IsDevMode)
            {
                StartServiceAsConsoleProcess(serviceName);
            }
            else
            {
                StartWindowsService(serviceName);
            }
        }

        public static void StopService()
        {
            StopService(TestServiceName);
        }

        public static void StopService(string serviceName)
        {
            if (GVFSTestConfig.IsDevMode)
            {
                StopConsoleServiceProcess(serviceName);
            }
            else
            {
                StopWindowsService(serviceName);
            }
        }

        private static void StartServiceAsConsoleProcess(string serviceName)
        {
            StopConsoleServiceProcess(serviceName);

            string pathToService = GetPathToService();
            Console.WriteLine("Starting test service in console mode: " + pathToService);

            ProcessStartInfo startInfo = new ProcessStartInfo(pathToService);
            startInfo.Arguments = $"--console --servicename={serviceName}";
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;

            Process consoleServiceProcess = Process.Start(startInfo);
            consoleServiceProcess.ShouldNotBeNull("Failed to start test service process");
            ConsoleServiceProcesses[serviceName] = consoleServiceProcess;

            // Consume output asynchronously to prevent buffer deadlock
            consoleServiceProcess.BeginOutputReadLine();
            consoleServiceProcess.BeginErrorReadLine();

            // Wait for the service to start listening on its named pipe
            string pipeName = serviceName + ".pipe";
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

        private static void StopConsoleServiceProcess(string serviceName)
        {
            Process consoleServiceProcess;
            if (ConsoleServiceProcesses.TryGetValue(serviceName, out consoleServiceProcess))
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
                }

                ConsoleServiceProcesses.Remove(serviceName);
            }
        }

        private static void CleanupServiceData(string serviceName)
        {
            string commonAppDataRoot = Environment.GetEnvironmentVariable("GVFS_COMMON_APPDATA_ROOT");
            string serviceData;
            if (!string.IsNullOrEmpty(commonAppDataRoot))
            {
                serviceData = Path.Combine(commonAppDataRoot, serviceName);
            }
            else
            {
                serviceData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "GVFS",
                    serviceName);
            }

            DirectoryInfo serviceDataDir = new DirectoryInfo(serviceData);
            if (serviceDataDir.Exists)
            {
                serviceDataDir.Delete(true);
            }
        }

        private static void InstallWindowsService(string serviceName)
        {
            Console.WriteLine("Installing " + serviceName);

            UninstallWindowsService(serviceName);

            // Wait for delete to complete. If the services control panel is open, this will never complete.
            while (RunScCommand("query", serviceName).ExitCode == 0)
            {
                Thread.Sleep(1000);
            }

            // Install service
            string pathToService = GetPathToService();
            Console.WriteLine("Using service executable: " + pathToService);

            File.Exists(pathToService).ShouldBeTrue($"{pathToService} does not exist");

            string createServiceArguments = string.Format(
                "{0} binPath= \"{1}\"",
                serviceName,
                pathToService);

            ProcessResult result = RunScCommand("create", createServiceArguments);
            result.ExitCode.ShouldEqual(0, "Failure while running sc create " + createServiceArguments + "\r\n" + result.Output);

            StartWindowsService(serviceName);
        }

        private static void UninstallWindowsService(string serviceName)
        {
            StopWindowsService(serviceName);

            RunScCommand("delete", serviceName);

            // Make sure to delete any test service data state
            string serviceData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GVFS", serviceName);
            DirectoryInfo serviceDataDir = new DirectoryInfo(serviceData);
            if (serviceDataDir.Exists)
            {
                serviceDataDir.Delete(true);
            }
        }

        private static void StartWindowsService(string serviceName)
        {
            ServiceController testService = ServiceController.GetServices().SingleOrDefault(service => service.ServiceName == serviceName);
            testService.ShouldNotBeNull($"{serviceName} does not exist as a service");

            using (ServiceController controller = new ServiceController(serviceName))
            {
                controller.Start(new[] { "--servicename=" + serviceName });
                controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                controller.Status.ShouldEqual(ServiceControllerStatus.Running);
            }
        }

        private static void StopWindowsService(string serviceName)
        {
            try
            {
                ServiceController testService = ServiceController.GetServices().SingleOrDefault(service => service.ServiceName == serviceName);
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
