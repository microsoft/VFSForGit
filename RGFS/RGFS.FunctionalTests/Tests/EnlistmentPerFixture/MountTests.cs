using RGFS.FunctionalTests.FileSystemRunners;
using RGFS.FunctionalTests.Should;
using RGFS.FunctionalTests.Tools;
using RGFS.Tests.Should;
using Microsoft.Win32.SafeHandles;
using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace RGFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    public class MountTests : TestsWithEnlistmentPerFixture
    {
        private const int RGFSGenericError = 3;
        private const uint GenericRead = 2147483648;
        private const uint FileFlagBackupSemantics = 3355443;
        private const string IndexLockPath = ".git\\index.lock";

        private FileSystemRunner fileSystem;

        public MountTests()
        {
            this.fileSystem = new SystemIORunner();
        }

        [TestCaseSource(typeof(MountSubfolders), MountSubfolders.MountFolders)]
        public void SecondMountAttemptFails(string mountSubfolder)
        {
            this.MountShouldFail(0, "already mounted", this.Enlistment.GetVirtualPathTo(mountSubfolder));
        }

        [TestCase]
        public void MountFailsOutsideEnlistment()
        {
            this.MountShouldFail("is not a valid RGFS enlistment", Path.GetDirectoryName(this.Enlistment.EnlistmentRoot));
        }

        [TestCase]
        public void MountCopiesMissingReadObjectHook()
        {
            this.Enlistment.UnmountRGFS();

            string readObjectPath = this.Enlistment.GetVirtualPathTo(@".git\hooks\read-object.exe");
            readObjectPath.ShouldBeAFile(this.fileSystem);
            this.fileSystem.DeleteFile(readObjectPath);
            readObjectPath.ShouldNotExistOnDisk(this.fileSystem);
            this.Enlistment.MountRGFS();
            readObjectPath.ShouldBeAFile(this.fileSystem);
        }

        [TestCase]
        public void MountSetsCoreHooksPath()
        {
            this.Enlistment.UnmountRGFS();

            GitProcess.Invoke(this.Enlistment.RepoRoot, "config --unset core.hookspath");
            string.IsNullOrWhiteSpace(
                GitProcess.Invoke(this.Enlistment.RepoRoot, "config core.hookspath"))
                .ShouldBeTrue();

            this.Enlistment.MountRGFS();
            string expectedHooksPath = Path.Combine(this.Enlistment.RepoRoot, ".git\\hooks");
            expectedHooksPath = expectedHooksPath.Replace("\\", "/");

            GitProcess.Invoke(
                this.Enlistment.RepoRoot, "config core.hookspath")
                .Trim('\n')
                .ShouldEqual(expectedHooksPath);
        }

        [TestCase]
        public void MountCleansStaleIndexLock()
        {
            this.MountCleansIndexLock(lockFileContents: "RGFS");
        }

        [TestCase]
        public void MountCleansEmptyIndexLock()
        {
            this.MountCleansIndexLock(lockFileContents: string.Empty);
        }

        [TestCase]
        public void MountCleansUnknownIndexLock()
        {
            this.MountCleansIndexLock(lockFileContents: "Bogus lock file contents");
        }

        [TestCase]
        public void MountFailsWhenNoOnDiskVersion()
        {
            this.Enlistment.UnmountRGFS();

            // Get the current disk layout version
            string currentVersion = RGFSHelpers.GetPersistedDiskLayoutVersion(this.Enlistment.DotRGFSRoot).ShouldNotBeNull();
            int currentVersionNum;
            int.TryParse(currentVersion, out currentVersionNum).ShouldEqual(true);

            // Move the RepoMetadata database to a temp file
            string versionDatabasePath = Path.Combine(this.Enlistment.DotRGFSRoot, RGFSHelpers.RepoMetadataName);
            versionDatabasePath.ShouldBeAFile(this.fileSystem);

            string tempDatabasePath = versionDatabasePath + "_MountFailsWhenNoOnDiskVersion";
            tempDatabasePath.ShouldNotExistOnDisk(this.fileSystem);

            this.fileSystem.MoveFile(versionDatabasePath, tempDatabasePath);
            versionDatabasePath.ShouldNotExistOnDisk(this.fileSystem);

            this.MountShouldFail("Failed to upgrade repo disk layout");

            // Move the RepoMetadata database back
            this.fileSystem.DeleteFile(versionDatabasePath);
            this.fileSystem.MoveFile(tempDatabasePath, versionDatabasePath);
            tempDatabasePath.ShouldNotExistOnDisk(this.fileSystem);
            versionDatabasePath.ShouldBeAFile(this.fileSystem);

            this.Enlistment.MountRGFS();
        }

        [TestCaseSource(typeof(MountSubfolders), MountSubfolders.MountFolders)]
        public void MountFailsAfterBreakingDowngrade(string mountSubfolder)
        {
            MountSubfolders.EnsureSubfoldersOnDisk(this.Enlistment, this.fileSystem);
            this.Enlistment.UnmountRGFS();

            string currentVersion = RGFSHelpers.GetPersistedDiskLayoutVersion(this.Enlistment.DotRGFSRoot).ShouldNotBeNull();
            int currentVersionNum;
            int.TryParse(currentVersion, out currentVersionNum).ShouldEqual(true);
            RGFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotRGFSRoot, (currentVersionNum + 1).ToString());

            this.MountShouldFail("do not allow mounting after downgrade", this.Enlistment.GetVirtualPathTo(mountSubfolder));

            RGFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotRGFSRoot, currentVersionNum.ToString());
            this.Enlistment.MountRGFS();
        }

        [TestCaseSource(typeof(MountSubfolders), MountSubfolders.MountFolders)]
        public void MountFailsUpgradingFromInvalidUpgradePath(string mountSubfolder)
        {
            MountSubfolders.EnsureSubfoldersOnDisk(this.Enlistment, this.fileSystem);
            string headCommitId = GitProcess.Invoke(this.Enlistment.RepoRoot, "rev-parse HEAD");

            this.Enlistment.UnmountRGFS();

            string currentVersion = RGFSHelpers.GetPersistedDiskLayoutVersion(this.Enlistment.DotRGFSRoot).ShouldNotBeNull();
            int currentVersionNum;
            int.TryParse(currentVersion, out currentVersionNum).ShouldEqual(true);

            // 1 will always be below the minumum support version number
            RGFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotRGFSRoot, "1");
            this.MountShouldFail("Breaking change to RGFS disk layout has been made since cloning", this.Enlistment.GetVirtualPathTo(mountSubfolder));

            RGFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotRGFSRoot, currentVersionNum.ToString());
            this.Enlistment.MountRGFS();
        }

        // Ported from GVFlt's BugRegressionTest
        [TestCase]
        public void GVFlt_CMDHangNoneActiveInstance()
        {
            this.Enlistment.UnmountRGFS();

            using (SafeFileHandle handle = NativeMethods.CreateFile(
                Path.Combine(this.Enlistment.RepoRoot, "aaa", "aaaa"),
                GenericRead,
                FileShare.Read,
                IntPtr.Zero,
                FileMode.Open,
                FileFlagBackupSemantics,
                IntPtr.Zero))
            {
                int lastError = Marshal.GetLastWin32Error();
                handle.IsInvalid.ShouldEqual(true);
                lastError.ShouldNotEqual(0); // 0 == ERROR_SUCCESS
            }
        }

        [TestCase]
        public void RepoIsExcludedFromAntiVirus()
        {
            bool isExcluded;
            string error; 
            this.TryGetIsPathExcluded(this.Enlistment.EnlistmentRoot, out isExcluded, out error).ShouldBeTrue("TryGetIsPathExcluded failed");
            isExcluded.ShouldBeTrue("Repo should be excluded from antivirus");
        }

        public bool TryGetIsPathExcluded(string path, out bool isExcluded, out string error)
        {
            isExcluded = false;
            try
            {
                string[] exclusions;
                if (this.TryGetKnownAntiVirusExclusions(out exclusions, out error))
                {
                    foreach (string excludedPath in exclusions)
                    {
                        if (excludedPath.Trim().Equals(path, StringComparison.OrdinalIgnoreCase))
                        {
                            isExcluded = true;
                            break;
                        }
                    }

                    return true;
                }

                return false;
            }
            catch (Exception e)
            {
                error = "Unable to get exclusions:" + e.ToString();
                return false;
            }
        }

        private bool TryGetKnownAntiVirusExclusions(out string[] exclusions, out string error)
        {
            ProcessStartInfo processInfo = new ProcessStartInfo();
            processInfo.FileName = "powershell.exe";
            processInfo.Arguments = "-NonInteractive -NoProfile -Command \"& { Get-MpPreference | Select -ExpandProperty ExclusionPath }\"";
            processInfo.WindowStyle = ProcessWindowStyle.Hidden;
            processInfo.CreateNoWindow = true;
            processInfo.WorkingDirectory = this.Enlistment.EnlistmentRoot;
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardOutput = true;

            ProcessResult getMpPrefrencesResult = ProcessHelper.Run(processInfo);

            // In some cases (like cmdlet not found), the exitCode == 0 but there will be errors and the output will be empty, handle this situation.
            if (getMpPrefrencesResult.ExitCode != 0 ||
                (string.IsNullOrEmpty(getMpPrefrencesResult.Output) && !string.IsNullOrEmpty(getMpPrefrencesResult.Errors)))
            {
                error = "Error while running PowerShell command to discover Defender exclusions. \n" + getMpPrefrencesResult.Errors;
                exclusions = null;
                return false;
            }

            exclusions = getMpPrefrencesResult.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            error = null;
            return true;
        }

        private void MountCleansIndexLock(string lockFileContents)
        {
            this.Enlistment.UnmountRGFS();

            string indexLockVirtualPath = this.Enlistment.GetVirtualPathTo(IndexLockPath);
            indexLockVirtualPath.ShouldNotExistOnDisk(this.fileSystem);

            if (string.IsNullOrEmpty(lockFileContents))
            {
                this.fileSystem.CreateEmptyFile(indexLockVirtualPath);
            }
            else
            {
                this.fileSystem.AppendAllText(indexLockVirtualPath, lockFileContents);
            }

            this.Enlistment.MountRGFS();
            this.Enlistment.WaitForBackgroundOperations().ShouldEqual(true, "Background operations failed to complete.");
            indexLockVirtualPath.ShouldNotExistOnDisk(this.fileSystem);
        }

        private void MountShouldFail(int expectedExitCode, string expectedErrorMessage, string mountWorkingDirectory = null)
        {
            string pathToRGFS = Path.Combine(TestContext.CurrentContext.TestDirectory, Properties.Settings.Default.PathToRGFS);
            string enlistmentRoot = this.Enlistment.EnlistmentRoot;

            // TODO: 865304 Use app.config instead of --internal* arguments
            ProcessStartInfo processInfo = new ProcessStartInfo(pathToRGFS);
            processInfo.Arguments = "mount --internal_use_only_service_name " + RGFSServiceProcess.TestServiceName;
            processInfo.WindowStyle = ProcessWindowStyle.Hidden;
            processInfo.WorkingDirectory = string.IsNullOrEmpty(mountWorkingDirectory) ? enlistmentRoot : mountWorkingDirectory;
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardOutput = true;

            ProcessResult result = ProcessHelper.Run(processInfo);
            result.ExitCode.ShouldEqual(expectedExitCode, $"mount exit code was not {expectedExitCode}. Output: {result.Output}");
            result.Output.ShouldContain(expectedErrorMessage);
        }

        private void MountShouldFail(string expectedErrorMessage, string mountWorkingDirectory = null)
        {
            this.MountShouldFail(RGFSGenericError, expectedErrorMessage, mountWorkingDirectory);
        }

        private class MountSubfolders
        {            
            public const string MountFolders = "Folders";
            private static object[] mountFolders =
            {
                new object[] { string.Empty },
                new object[] { "RGFS" },
            };

            public static object[] Folders
            {
                get
                {
                    return mountFolders;
                }
            }

            public static void EnsureSubfoldersOnDisk(RGFSFunctionalTestEnlistment enlistment, FileSystemRunner fileSystem)
            {
                // Enumerate the directory to ensure that the folder is on disk after RGFS is unmounted
                foreach (object[] folder in Folders)
                {
                    string folderPath = enlistment.GetVirtualPathTo((string)folder[0]);
                    folderPath.ShouldBeADirectory(fileSystem).WithItems();
                }
            }
        }
    }
}
