using CommandLine;
using System;
using System.Linq;

namespace MirrorProvider
{
    public static class MirrorProviderCLI
    {
        public static void Run(string[] args, FileSystemVirtualizer fileSystemVirtualizer)
        {
            new Parser(
                settings =>
                {
                    settings.CaseSensitive = false;
                    settings.EnableDashDash = true;
                    settings.IgnoreUnknownArguments = false;
                    settings.HelpWriter = Console.Error;
                })
                .ParseArguments(args, typeof(CloneVerb), typeof(MountVerb))
                .WithNotParsed(
                    errors =>
                    {
                        if (errors.Any(error => error is TokenError))
                        {
                            Environment.Exit(1);
                        }
                    })
                .WithParsed<CloneVerb>(clone => clone.Execute(fileSystemVirtualizer))
                .WithParsed<MountVerb>(mount => mount.Execute(fileSystemVirtualizer));
        }
    }
}
