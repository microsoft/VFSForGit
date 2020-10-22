using CommandLine;
using GVFS.Common;
using GVFS.Platform.Windows;

namespace GVFS.Upgrader
{
    public class Program
    {
        public static void Main(string[] args)
        {
            GVFSPlatform.Register(new WindowsPlatform());

            Parser.Default.ParseArguments<UpgradeOptions>(args)
                .WithParsed(options =>  UpgradeOrchestratorFactory.Create(options).Execute());
        }
    }
}
