namespace GVFS.Common
{
    public interface IInstallerPreRunChecker
    {
        bool TryRunPreUpgradeChecks(out string consoleError);

        bool TryMountAllGVFSRepos(out string consoleError);

        bool TryUnmountAllGVFSRepos(out string consoleError);

        bool IsInstallationBlockedByRunningProcess(out string consoleError);
    }
}
