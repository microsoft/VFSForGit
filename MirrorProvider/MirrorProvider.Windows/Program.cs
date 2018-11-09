namespace MirrorProvider.Windows
{
    class Program
    {
        static void Main(string[] args)
        {
            MirrorProviderCLI.Run(args, new WindowsFileSystemVirtualizer());
        }
    }
}
