using CommandLine;
using GVFS.CommandLine;
using System;

namespace GVFS
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Type[] verbTypes = new Type[]
            {
                // Verbs that work without an existing enlistment
                typeof(CloneVerb),

                // Verbs that require an existing enlistment
                typeof(DiagnoseVerb),
                typeof(LogVerb),
                typeof(MountVerb),
                typeof(PrefetchVerb),
                typeof(StatusVerb),
                typeof(UnmountVerb),
            };

            try
            {
                new Parser(
                    settings =>
                    {
                        settings.CaseSensitive = false;
                        settings.EnableDashDash = true;
                        settings.IgnoreUnknownArguments = false;
                        settings.HelpWriter = Console.Error;
                    })
                    .ParseArguments(args, verbTypes)
                    .WithParsed<GVFSVerb>(verb => verb.Execute());
            }
            catch (GVFSVerb.VerbAbortedException e)
            {
                // Calling Environment.Exit() is required, to force all background threads to exit as well
                Environment.Exit((int)e.Verb.ReturnCode);
            }
        }
    }
}
