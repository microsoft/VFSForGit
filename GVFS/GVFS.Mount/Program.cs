using System.CommandLine;
using System.Runtime.CompilerServices;
using GVFS.PlatformLoader;
using System;

[assembly: InternalsVisibleTo("GVFS.CommandLine.Tests")]

namespace GVFS.Mount
{
    public class Program
    {
        public static void Main(string[] args)
        {
            GVFSPlatformLoader.Initialize();
            try
            {
                RootCommand rootCommand = BuildRootCommand();
                rootCommand.Parse(args).Invoke();
            }
            catch (MountAbortedException e)
            {
                // Calling Environment.Exit() is required, to force all background threads to exit as well
                Environment.Exit((int)e.Verb.ReturnCode);
            }
        }

        internal static RootCommand BuildRootCommand() => InProcessMountVerb.BuildRootCommand();
    }
}
