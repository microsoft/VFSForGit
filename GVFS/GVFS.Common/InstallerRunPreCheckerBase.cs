namespace GVFS.Common
{
    public abstract class InstallerRunPreCheckerBase
    {
        public abstract bool TryRunPreUpgradeChecks(out string consoleError);

        public abstract bool TryMountAllGVFSRepos(out string consoleError);

        public abstract bool TryUnmountAllGVFSRepos(out string consoleError);

        public abstract bool IsInstallationBlockedByRunningProcess(out string consoleError);
    }
}
