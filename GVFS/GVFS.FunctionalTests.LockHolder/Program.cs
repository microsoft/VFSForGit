using CommandLine;
using GVFS.PlatformLoader;

namespace GVFS.FunctionalTests.LockHolder
{
    public class Program
    {
        public static void Main(string[] args)
        {
            GVFSPlatformLoader.Initialize();

            Parser.Default.ParseArguments<AcquireGVFSLockVerb>(args)
                    .WithParsed(acquireGVFSLock => acquireGVFSLock.Execute());
        }
    }
}
