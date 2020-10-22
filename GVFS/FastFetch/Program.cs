using CommandLine;
using GVFS.Common;
using GVFS.Platform.Windows;

namespace FastFetch
{
    public class Program
    {
        public static void Main(string[] args)
        {
            GVFSPlatform.Register(new WindowsPlatform());
            Parser.Default.ParseArguments<FastFetchVerb>(args)
                .WithParsed(fastFetch => fastFetch.Execute());
        }
    }
}
