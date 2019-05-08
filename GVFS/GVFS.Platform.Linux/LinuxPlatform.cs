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
                get
                {
                    ProcessResult result = ProcessHelper.Run("which", args: this.GVFSExecutableName, redirectOutput: true);
                    if (result.ExitCode != 0)
                    {
                        // TODO(Linux): avoid constants for paths entirely
                        return "/usr/local/vfsforgit";
                    }

                    return Path.GetDirectoryName(result.Output.Trim());
                }
            }

            public override string GVFSBinDirectoryName
            {
                get { return Path.GetFileName(this.GVFSBinDirectoryPath); }
            }
        }
    }
}
