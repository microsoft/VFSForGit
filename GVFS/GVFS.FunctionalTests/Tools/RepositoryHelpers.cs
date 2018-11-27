using GVFS.FunctionalTests.FileSystemRunners;
using System.Runtime.InteropServices;

namespace GVFS.FunctionalTests.Tools
{
    public static class RepositoryHelpers
    {
        public static void DeleteTestRepository(string repoPath)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                CmdRunner.DeleteDirectoryWithUnlimitedRetries(repoPath);
            }
            else
            {
                BashRunner.DeleteDirectoryWithUnlimitedRetries(repoPath);
            }
        }
    }
}
