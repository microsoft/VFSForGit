using CommandLine;
using System;

namespace GVFS.Mount
{
    public class Program
    {
        public static void Main(string[] args)
        {
            try
            {
                Parser.Default.ParseArguments<MountVerb>(args)
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
