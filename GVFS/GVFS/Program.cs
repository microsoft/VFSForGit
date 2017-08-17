using CommandLine;
using GVFS.CommandLine;
using System;

// This is to keep the reference to GVFS.Mount
// so that the exe will end up in the output directory of GVFS
using GVFS.Mount;

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
                typeof(CacheServerVerb),
                typeof(DehydrateVerb),
                typeof(DiagnoseVerb),
                typeof(LogVerb),
                typeof(MountVerb),
                typeof(PrefetchVerb),
                typeof(RepairVerb),
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
                    .WithParsed<CloneVerb>(
                        clone =>
                        {
                            // We handle the clone verb differently, because clone cares if the enlistment path
                            // was not specified vs if it was specified to be the current directory
                            clone.Execute();
                        })
                    .WithParsed<GVFSVerb>(
                        verb =>
                        {
                            // For all other verbs, they don't care if the enlistment root is explicitly
                            // specified or implied to be the current directory
                            if (string.IsNullOrEmpty(verb.EnlistmentRootPath))
                            {
                                verb.EnlistmentRootPath = Environment.CurrentDirectory;
                            }

                            verb.Execute();
                        });
            }
            catch (GVFSVerb.VerbAbortedException e)
            {
                // Calling Environment.Exit() is required, to force all background threads to exit as well
                Environment.Exit((int)e.Verb.ReturnCode);
            }
        }
    }
}
