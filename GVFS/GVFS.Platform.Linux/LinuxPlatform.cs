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
        {
        }

        public override IDiskLayoutUpgradeData DiskLayoutUpgrade { get; } = new LinuxDiskLayoutUpgradeData();
        public override IKernelDriver KernelDriver { get; } = new ProjFSLib();
        public override string Name { get => "Linux"; }
        public override GVFSPlatformConstants Constants { get; } = new LinuxPlatformConstants();
        public override IPlatformFileSystem FileSystem { get; } = new LinuxFileSystem();

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

        public override FileBasedLock CreateFileBasedLock(
            PhysicalFileSystem fileSystem,
            ITracer tracer,
            string lockPath)
        {
            return new LinuxFileBasedLock(fileSystem, tracer, lockPath);
        }

        public class LinuxPlatformConstants : POSIXPlatformConstants
        {
            public override string InstallerExtension
            {
                get { return ".deb"; }
            }

            public override string WorkingDirectoryBackingRootPath
            {
                get { return Path.Combine(GVFSConstants.DotGVFS.Root, "lower"); }
            }

            public override string GVFSBinDirectoryPath
            {
                get { return Path.Combine("/usr", "local", this.GVFSBinDirectoryName); }
            }

            public override string GVFSBinDirectoryName
            {
                get { return "vfsforgit"; }
            }
        }
    }
}