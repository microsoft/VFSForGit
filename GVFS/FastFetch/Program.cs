using System.CommandLine;
using GVFS.PlatformLoader;

namespace FastFetch
{
    public class Program
    {
        public static void Main(string[] args)
        {
            GVFSPlatformLoader.Initialize();
            RootCommand rootCommand = FastFetchVerb.BuildRootCommand();
            rootCommand.Parse(args).Invoke();
        }
    }
}
