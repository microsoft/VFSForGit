using CommandLine;

namespace GVFS.FunctionalTests.LockHolder
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Parser.Default.ParseArguments<AcquireGVFSLockVerb>(args)
                    .WithParsed(acquireGVFSLock => acquireGVFSLock.Execute());
        }
    }
}
