using CommandLine;
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
                Parser.Default.ParseArguments<InProcessMountVerb>(args)
                    .WithParsed(mount => mount.Execute());
            }
            catch (MountAbortedException e)
            {
                // Calling Environment.Exit() is required, to force all background threads to exit as well
                Environment.Exit((int)e.Verb.ReturnCode);
            }
        }
    }
}
