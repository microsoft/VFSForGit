namespace GVFS.Upgrader
{
    public class LinuxUpgradeOrchestrator : UpgradeOrchestrator
    {
        public LinuxUpgradeOrchestrator(UpgradeOptions options)
        : base(options)
        {
        }

        protected override bool TryMountRepositories(out string consoleError)
        {
            // Linux upgrader does not mount repositories
            consoleError = null;
            return true;
        }
    }
}
