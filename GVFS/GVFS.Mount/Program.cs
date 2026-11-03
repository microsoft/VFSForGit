using System.CommandLine;
using System.Runtime.CompilerServices;
using GVFS.Common;
using GVFS.PlatformLoader;
using System;

[assembly: InternalsVisibleTo("GVFS.CommandLine.Tests")]

namespace GVFS.Mount
{
    public class Program
    {
        public static int Main(string[] args)
        {
            GVFSPlatformLoader.Initialize();

            RootCommand rootCommand = BuildRootCommand();

            // The root command's action catches MountAbortedException and maps it to
            // its ReturnCode. Any other exception that escapes the action is caught
            // by System.CommandLine itself, which still returns a nonzero code (1).
            // Either way, Invoke()'s result is the real outcome of the mount attempt,
            // so it must become our process exit code instead of being discarded.
            int exitCode = rootCommand.Parse(args).Invoke();

            if (exitCode != (int)ReturnCode.Success)
            {
                // Calling Environment.Exit() is required, to force all background threads to exit as well
                Environment.Exit(exitCode);
            }

            return exitCode;
        }

        internal static RootCommand BuildRootCommand() => InProcessMountVerb.BuildRootCommand();
    }
}
