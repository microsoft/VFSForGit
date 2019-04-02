using GVFS.Common;
using GVFS.Common.FileSystem;

namespace GVFS.Platform.Mac
{
    public partial class MacPlatform : POSIX.POSIXPlatform
    {
        public MacPlatform()
            : base(
                installerExtension: "." + InstallerExtension)
        {
        }

        public override IKernelDriver KernelDriver { get; } = new ProjFSKext();

        public override string GetOSVersionInformation()
        {
            ProcessResult result = ProcessHelper.Run("sw_vers", args: string.Empty, redirectOutput: true);
            return string.IsNullOrWhiteSpace(result.Output) ? result.Errors : result.Output;
        }
    }
}
