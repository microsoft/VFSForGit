namespace GVFS.Upgrader
{
    public static class UpgradeOrchestratorFactory
    {
        public static UpgradeOrchestrator Create(UpgradeOptions options)
        {
            return new WindowsUpgradeOrchestrator(options);
        }
    }
}
