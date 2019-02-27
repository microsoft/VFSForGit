using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    [NonParallelizable]
    [Category(Categories.FullSuiteOnly)]
    [Category(Categories.WindowsOnly)]
    public class UpgradeReminderTests : TestsWithEnlistmentPerFixture
    {
        private const string HighestAvailableVersionFileName = "HighestAvailableVersion";
        private const string UpgradeRingKey = "upgrade.ring";
        private const string AlwaysUpToDateRing = "None";

        private string upgradeDownloadsDirectory;
        private FileSystemRunner fileSystem;

        public UpgradeReminderTests()
        {
            this.fileSystem = new SystemIORunner();
            this.upgradeDownloadsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolderOption.Create),
                "GVFS",
                "GVFS.Upgrade",
                "Downloads");
        }

        [TestCase]
        public void NoReminderWhenUpgradeNotAvailable()
        {
            this.EmptyDownloadDirectory();

            for (int count = 0; count < 50; count++)
            {
                ProcessResult result = GitHelpers.InvokeGitAgainstGVFSRepo(
                    this.Enlistment.RepoRoot,
                    "status");

                string.IsNullOrEmpty(result.Errors).ShouldBeTrue();
            }
        }

        [TestCase]
        public void RemindWhenUpgradeAvailable()
        {
            this.CreateUpgradeAvailableMarkerFile();
            this.ReminderMessagingEnabled().ShouldBeTrue();
            this.EmptyDownloadDirectory();
        }

        [TestCase]
        public void NoReminderForLeftOverDownloads()
        {
            this.VerifyServiceRestartStopsReminder();
            this.VerifyUpgradeVerbStopsReminder();
        }

        [TestCase]
        public void UpgradeTimerScheduledOnServiceStart()
        {
            this.RestartService();

            bool timerScheduled = false;
            for (int trialCount = 0; trialCount < 15; trialCount++)
            {
                Thread.Sleep(TimeSpan.FromSeconds(1));
                if (this.ServiceLogContainsUpgradeMessaging())
                {
                    timerScheduled = true;
                    break;
                }
            }

            timerScheduled.ShouldBeTrue();
        }

        private bool ServiceLogContainsUpgradeMessaging()
        {
            // This test checks for the upgrade timer start message in the Service log
            // file. GVFS.Service should schedule the timer as it starts.
            string expectedTimerMessage = "Checking for product upgrades. (Start)";
            string serviceLogFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "GVFS",
                GVFSServiceProcess.TestServiceName,
                "Logs");
            DirectoryInfo logsDirectory = new DirectoryInfo(serviceLogFolder);
            FileInfo logFile = logsDirectory.GetFiles()
                .OrderByDescending(f => f.LastWriteTime)
                .FirstOrDefault();

            if (logFile != null)
            {
                using (StreamReader fileStream = new StreamReader(File.Open(logFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                {
                    string nextLine = null;
                    while ((nextLine = fileStream.ReadLine()) != null)
                    {
                        if (nextLine.Contains(expectedTimerMessage))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private void EmptyDownloadDirectory()
        {
            if (Directory.Exists(this.upgradeDownloadsDirectory))
            {
                Directory.Delete(this.upgradeDownloadsDirectory, recursive: true);
            }

            Directory.CreateDirectory(this.upgradeDownloadsDirectory);
            Directory.Exists(this.upgradeDownloadsDirectory).ShouldBeTrue();
            Directory.EnumerateFiles(this.upgradeDownloadsDirectory).Any().ShouldBeFalse();
        }

        private void CreateUpgradeAvailableMarkerFile()
        {
            string gvfsUpgradeAvailableFilePath = Path.Combine(
                Path.GetDirectoryName(this.upgradeDownloadsDirectory),
                HighestAvailableVersionFileName);

            this.EmptyDownloadDirectory();

            this.fileSystem.CreateEmptyFile(gvfsUpgradeAvailableFilePath);
            this.fileSystem.FileExists(gvfsUpgradeAvailableFilePath).ShouldBeTrue();
        }

        private void SetUpgradeRing(string value)
        {
            this.RunGVFS($"config {UpgradeRingKey} {value}");
        }

        private string RunUpgradeCommand()
        {
            return this.RunGVFS("upgrade");
        }

        private string RunGVFS(string argument)
        {
            ProcessResult result = ProcessHelper.Run(GVFSTestConfig.PathToGVFS, argument);
            result.ExitCode.ShouldEqual(0, result.Errors);

            return result.Output;
        }

        private void RestartService()
        {
            GVFSServiceProcess.StopService();
            GVFSServiceProcess.StartService();
        }

        private bool ReminderMessagingEnabled()
        {
            for (int count = 0; count < 50; count++)
            {
                ProcessResult result = GitHelpers.InvokeGitAgainstGVFSRepo(
                this.Enlistment.RepoRoot,
                "status",
                removeWaitingMessages:true,
                removeUpgradeMessages:false);

                if (!string.IsNullOrEmpty(result.Errors) &&
                    result.Errors.Contains("A new version of GVFS is available."))
                {
                    return true;
                }
            }

            return false;
        }

        private void VerifyServiceRestartStopsReminder()
        {
            this.CreateUpgradeAvailableMarkerFile();
            this.ReminderMessagingEnabled().ShouldBeTrue();
            this.SetUpgradeRing(AlwaysUpToDateRing);
            this.RestartService();

            // Wait for sometime so service can detect product is up-to-date and delete left over downloads
            TimeSpan timeToWait = TimeSpan.FromMinutes(1);
            bool reminderMessagingEnabled = true;
            while ((reminderMessagingEnabled = this.ReminderMessagingEnabled()) && timeToWait > TimeSpan.Zero)
            {
                Thread.Sleep(TimeSpan.FromSeconds(5));
                timeToWait = timeToWait.Subtract(TimeSpan.FromSeconds(5));
            }

            reminderMessagingEnabled.ShouldBeFalse();
        }

        private void VerifyUpgradeVerbStopsReminder()
        {
            this.SetUpgradeRing(AlwaysUpToDateRing);
            this.CreateUpgradeAvailableMarkerFile();
            this.ReminderMessagingEnabled().ShouldBeTrue();
            this.RunUpgradeCommand();
            this.ReminderMessagingEnabled().ShouldBeFalse();
        }
    }
}
