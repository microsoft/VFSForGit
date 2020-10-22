using CommandLine;
using GVFS.Common;
using GVFS.Platform.Windows;
using System;

namespace GVFS.Mount
{
    public class Program
    {
        public static void Main(string[] args)
        {
            GVFSPlatform.Register(new WindowsPlatform());
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
