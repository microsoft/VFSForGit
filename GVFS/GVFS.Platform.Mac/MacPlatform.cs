using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using GVFS.Platform.POSIX;

namespace GVFS.Platform.Mac
{
    public partial class MacPlatform : POSIXPlatform
    {
        public MacPlatform() : base(installerExtension: ".dmg", storagePath: GVFSConstants.WorkingDirectoryRootName)
        {
        }

        public override IKernelDriver KernelDriver { get; } = new ProjFSKext();
        public override IDiskLayoutUpgradeData DiskLayoutUpgrade { get; } = new MacDiskLayoutUpgradeData();
        public override IPlatformFileSystem FileSystem { get; } = new MacFileSystem();

        public override string GetOSVersionInformation()
        {
            ProcessResult result = ProcessHelper.Run("sw_vers", args: string.Empty, redirectOutput: true);
            return string.IsNullOrWhiteSpace(result.Output) ? result.Errors : result.Output;
        }

        public override string GetDataRootForGVFS()
        {
            return MacPlatform.GetDataRootForGVFSImplementation();
        }

        public override string GetDataRootForGVFSComponent(string componentName)
        {
            return MacPlatform.GetDataRootForGVFSComponentImplementation(componentName);
        }

        public override void InitializeStorageMapping(string dotGVFSRoot, string workingDirectoryRoot)
        {
        }

        public override FileBasedLock CreateFileBasedLock(
            PhysicalFileSystem fileSystem,
            ITracer tracer,
            string lockPath)
        {
            return new MacFileBasedLock(fileSystem, tracer, lockPath);
        }
    }
}
