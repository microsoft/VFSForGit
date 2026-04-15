using System.CommandLine;
using GVFS.PlatformLoader;
using System;

namespace GVFS.Mount
{
    public class Program
    {
        public static void Main(string[] args)
        {
            GVFSPlatformLoader.Initialize();
            try
            {
                RootCommand rootCommand = InProcessMountVerb.BuildRootCommand();
                rootCommand.Parse(args).Invoke();
            }
            catch (MountAbortedException e)
            {
                // Calling Environment.Exit() is required, to force all background threads to exit as well
                Environment.Exit((int)e.Verb.ReturnCode);
            }
        }
    }
}
