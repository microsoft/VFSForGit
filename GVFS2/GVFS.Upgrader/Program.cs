using CommandLine;
using GVFS.PlatformLoader;

namespace GVFS.Upgrader
{
    public class Program
    {
        public static void Main(string[] args)
        {
            GVFSPlatformLoader.Initialize();

            Parser.Default.ParseArguments<UpgradeOptions>(args)
                .WithParsed(options =>  UpgradeOrchestratorFactory.Create(options).Execute());
        }
    }
}
