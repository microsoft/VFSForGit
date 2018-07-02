using CommandLine;
using GVFS.PlatformLoader;

namespace FastFetch
{
    public class Program
    {
        public static void Main(string[] args)
        {
            GVFSPlatformLoader.Initialize();
            Parser.Default.ParseArguments<FastFetchVerb>(args)
                .WithParsed(fastFetch => fastFetch.Execute());
        }
    }
}
