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
        public const string StorageConfigName = "vfsforgit-projfs.conf";

        private string storageConfigPath;

        public LinuxPlatform()
            : base(
                installerExtension: string.Empty)
        {
        }

        public override IKernelDriver KernelDriver { get; } = new ProjFSLib();
        public override IDiskLayoutUpgradeData DiskLayoutUpgrade { get; } = new LinuxDiskLayoutUpgradeData();
        public override IPlatformFileSystem FileSystem { get; } = new LinuxFileSystem();

        public override string GetOSVersionInformation()
        {
            ProcessResult result = ProcessHelper.Run("uname", args: "-srv", redirectOutput: true);
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

        public override void InitializeStorageMapping(string dotGVFSRoot, string workingDirectoryRoot)
        {
            // TODO(Linux): set this path in common class
            string storageRoot = Path.Combine(dotGVFSRoot, "lower");

            this.storageConfigPath = Path.Combine(workingDirectoryRoot, StorageConfigName);

            FileInfo storageConfig = new FileInfo(this.storageConfigPath);
            using (FileStream fs = storageConfig.Create())
            using (StreamWriter writer = new StreamWriter(fs))
            {
                writer.Write(
$@"# This source directory was created by the VFSForGit 'clone' command,
# and its contents will be projected (virtualized) by running the
# VFSForGit 'mount' command.
#
# Do not be alarmed if the directory appears empty except for this file!
# Running the VFSForGit 'mount' command in the parent directory will
# cause this directory to be mounted and the contents projected into place.

# Lower storage directory; used by projfs when virtualizing.
lowerdir={storageRoot}
");
            }
        }

        public override FileBasedLock CreateFileBasedLock(
            PhysicalFileSystem fileSystem,
            ITracer tracer,
            string lockPath)
        {
            return new LinuxFileBasedLock(fileSystem, tracer, lockPath);
        }
    }
}
