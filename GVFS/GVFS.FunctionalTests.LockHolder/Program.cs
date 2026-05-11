using System.CommandLine;

namespace GVFS.FunctionalTests.LockHolder
{
    public class Program
    {
        public static void Main(string[] args)
        {
            RootCommand rootCommand = AcquireGVFSLockVerb.BuildRootCommand();
            rootCommand.Parse(args).Invoke();
        }
    }
}
