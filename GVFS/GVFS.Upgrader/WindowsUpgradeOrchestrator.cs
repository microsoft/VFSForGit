using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using System.IO;

namespace GVFS.Upgrader
{
    public class WindowsUpgradeOrchestrator : UpgradeOrchestrator
    {
        public WindowsUpgradeOrchestrator(
            ProductUpgrader upgrader,
            ITracer tracer,
            PhysicalFileSystem fileSystem,
            InstallerPreRunChecker preRunChecker,
            TextReader input,
            TextWriter output)
            : base(upgrader, tracer, fileSystem, preRunChecker, input, output)
        {
        }

        public WindowsUpgradeOrchestrator(UpgradeOptions options)
            : base(options)
        {
        }

        protected override bool TryMountRepositories(out string consoleError)
        {
            string errorMessage = string.Empty;
            if (this.mount && !this.LaunchInsideSpinner(
                () =>
                {
                    string mountError;
                    if (!this.preRunChecker.TryMountAllGVFSRepos(out mountError))
                    {
                        EventMetadata metadata = new EventMetadata();
                        metadata.Add("Upgrade Step", nameof(this.TryMountRepositories));
                        metadata.Add("Mount Error", mountError);
                        this.tracer.RelatedError(metadata, $"{nameof(this.preRunChecker.TryMountAllGVFSRepos)} failed.");
                        errorMessage += mountError;
                        return false;
                    }

                    return true;
                },
                "Mounting repositories"))
            {
                consoleError = errorMessage;
                return false;
            }

            consoleError = null;
            return true;
        }
    }
}
