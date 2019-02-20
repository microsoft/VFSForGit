using System;

namespace GVFS.Upgrader
{
    public static class UpgradeOrchestratorFactory
    {
        public static UpgradeOrchestrator Create(UpgradeOptions options)
        {
#if LINUX_BUILD
            return new LinuxUpgradeOrchestrator(options);
#elif MACOS_BUILD
            return new MacUpgradeOrchestrator(options);
#elif WINDOWS_BUILD
            return new WindowsUpgradeOrchestrator(options);
#else
            throw new NotImplementedException();
#endif
            }
    }
}
