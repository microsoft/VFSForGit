using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using GVFS.Platform.POSIX;
using System.Collections.Generic;
using System.IO;

namespace GVFS.Platform.Linux
{
    public partial class LinuxPlatform : POSIXPlatform
    {
        // TODO(Linux): determine installation location and upgrader path
        private const string UpgradeProtectedDataDirectory = "/usr/local/vfsforgit_upgrader";

        public LinuxPlatform()
        {
        }

        public override IDiskLayoutUpgradeData DiskLayoutUpgrade { get; } = new LinuxDiskLayoutUpgradeData();
        public override IKernelDriver KernelDriver { get; } = new ProjFSLib();
        public override string Name { get => "Linux"; }
        public override GVFSPlatformConstants Constants { get; } = new LinuxPlatformConstants();
        public override IPlatformFileSystem FileSystem { get; } = new LinuxFileSystem();

        public override string GVFSConfigPath
        {
            get
            {
                return Path.Combine(this.Constants.GVFSBinDirectoryPath, LocalGVFSConfig.FileName);
            }
        }

        /// <summary>
        /// On Linux VFSForGit does not need to use system wide logs to track
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

        public override string GetOSVersionInformation()
        {
            ProcessResult result = ProcessHelper.Run("sw_vers", args: string.Empty, redirectOutput: true);
            return string.IsNullOrWhiteSpace(result.Output) ? result.Errors : result.Output;
        }

        public override string GetDataRootForGVFS()
        {
            return LinuxPlatform.GetDataRootForGVFSImplementation();
        }

        public override string GetDataRootForGVFSComponent(string componentName)
        {
            return LinuxPlatform.GetDataRootForGVFSComponentImplementation(componentName);
        }

        public override bool TryGetGVFSEnlistmentRoot(string directory, out string enlistmentRoot, out string errorMessage)
        {
            return LinuxPlatform.TryGetGVFSEnlistmentRootImplementation(directory, out enlistmentRoot, out errorMessage);
        }

        public override string GetNamedPipeName(string enlistmentRoot)
        {
            return LinuxPlatform.GetNamedPipeNameImplementation(enlistmentRoot);
        }

        public override FileBasedLock CreateFileBasedLock(
            PhysicalFileSystem fileSystem,
            ITracer tracer,
            string lockPath)
        {
            return new LinuxFileBasedLock(fileSystem, tracer, lockPath);
        }

        public override string GetUpgradeProtectedDataDirectory()
        {
            return UpgradeProtectedDataDirectory;
        }

        public override string GetUpgradeHighestAvailableVersionDirectory()
        {
            return GetUpgradeHighestAvailableVersionDirectoryImplementation();
        }

        /// <summary>
        /// This is the directory in which the upgradelogs directory should go.
        /// There can be multiple logs directories, so here we return the containing
        /// directory.
        /// </summary>
        public override string GetUpgradeLogDirectoryParentDirectory()
        {
            return this.GetUpgradeNonProtectedDataDirectory();
        }

        public override string GetSystemInstallerLogPath()
        {
            return null;
        }

        public override ProductUpgraderPlatformStrategy CreateProductUpgraderPlatformInteractions(
            PhysicalFileSystem fileSystem,
            ITracer tracer)
        {
            return new LinuxProductUpgraderPlatformStrategy(fileSystem, tracer);
        }

        public class LinuxPlatformConstants : POSIXPlatformConstants
        {
            public override string InstallerExtension
            {
                get { return ".deb"; }
            }

            public override string WorkingDirectoryBackingRootPath
            {
                get { return Path.Combine(this.DotGVFSRoot, "lower"); }
            }

            public override string DotGVFSRoot
            {
                get { return LinuxPlatform.DotGVFSRoot; }
            }

            public override string GVFSBinDirectoryPath
            {
                get { return Path.Combine("/usr", "local", this.GVFSBinDirectoryName); }
            }

            public override string GVFSBinDirectoryName
            {
                get { return "vfsforgit"; }
            }

            public override string UpgradeInstallAdviceMessage
            {
                get { return $"When ready, run {this.UpgradeConfirmCommandMessage} to upgrade."; }
            }

            public override string UpgradeConfirmCommandMessage
            {
                get { return UpgradeConfirmMessage; }
            }

            public override string StartServiceCommandMessage
            {
                // TODO(Linux): implement service daemon
                get { return "Not yet implemented"; }
            }

            public override string RunUpdateMessage
            {
                get { return $"Run {UpgradeConfirmMessage}."; }
            }

            public override bool CaseSensitiveFileSystem => true;
        }
    }
}
