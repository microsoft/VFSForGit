namespace MirrorProvider.Linux
{
    class Program
    {
        static void Main(string[] args)
        {
            MirrorProviderCLI.Run(args, new LinuxFileSystemVirtualizer());
        }
    }
}
