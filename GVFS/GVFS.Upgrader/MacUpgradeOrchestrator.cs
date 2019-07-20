namespace GVFS.Upgrader
{
    public class MacUpgradeOrchestrator : UpgradeOrchestrator
    {
        public MacUpgradeOrchestrator(UpgradeOptions options)
        : base(options)
        {
        }

        protected override bool TryMountRepositories(out string consoleError)
        {
            // Mac upgrader does not mount repositories
            consoleError = null;
            return true;
        }
    }
}
