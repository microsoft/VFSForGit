using System.CommandLine;
using System.Runtime.CompilerServices;
using GVFS.PlatformLoader;

[assembly: InternalsVisibleTo("GVFS.CommandLine.Tests")]

namespace FastFetch
{
    public class Program
    {
        public static void Main(string[] args)
        {
            GVFSPlatformLoader.Initialize();
            RootCommand rootCommand = BuildRootCommand();
            rootCommand.Parse(args).Invoke();
        }

        internal static RootCommand BuildRootCommand() => FastFetchVerb.BuildRootCommand();
    }
}
