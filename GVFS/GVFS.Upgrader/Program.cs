using GVFS.PlatformLoader;

namespace GVFS.Upgrader
{
    public class Program
    {
        public static void Main(string[] args)
        {
            GVFSPlatformLoader.Initialize();

            UpgradeOrchestrator upgrader = new UpgradeOrchestrator();

            upgrader.Execute();
        }
    }
}
