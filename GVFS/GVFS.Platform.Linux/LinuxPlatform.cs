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

        public override FileBasedLock CreateFileBasedLock(
            PhysicalFileSystem fileSystem,
            ITracer tracer,
            string lockPath)
        {
            return new LinuxFileBasedLock(fileSystem, tracer, lockPath);
        }
    }
}
