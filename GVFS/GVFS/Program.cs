using System.CommandLine;
using GVFS.CommandLine;
using GVFS.Common;
using GVFS.PlatformLoader;
using System;

namespace GVFS
{
    public class Program
    {
        public static void Main(string[] args)
        {
            GVFSPlatformLoader.Initialize();
            if (!GVFSPlatform.Instance.KernelDriver.RegisterForOfflineIO())
            {
                Console.WriteLine("Unable to register with the kernel for offline I/O. Ensure that VFS for Git installed successfully and try again");
                Environment.Exit((int)ReturnCode.UnableToRegisterForOfflineIO);
            }

            // Normalize verb name to lowercase for case-insensitive matching.
            // The old CommandLineParser had CaseSensitive = false; System.CommandLine
            // is case-sensitive, so we normalize the first non-option argument.
            if (args.Length > 0 && !args[0].StartsWith("-"))
            {
                args[0] = args[0].ToLowerInvariant();
            }

            try
            {
                RootCommand rootCommand = BuildRootCommand();
                int exitCode = rootCommand.Parse(args).Invoke();

                // If a verb executed successfully, its SetAction already called Environment.Exit.
                // If we reach here, it means parsing failed or help was shown.
                if (exitCode != 0)
                {
                    Environment.Exit((int)ReturnCode.ParsingError);
                }
            }
            catch (GVFSVerb.VerbAbortedException e)
            {
                // Calling Environment.Exit() is required, to force all background threads to exit as well
                Environment.Exit((int)e.Verb.ReturnCode);
            }
            finally
            {
                if (!GVFSPlatform.Instance.KernelDriver.UnregisterForOfflineIO())
                {
                    Console.WriteLine("Unable to unregister with the kernel for offline I/O.");
                }
            }
        }

        internal static RootCommand BuildRootCommand()
        {
            RootCommand rootCommand = new RootCommand("VFS for Git: Enable Git at Enterprise Scale");

            // Remove System.CommandLine's built-in --version option and replace
            // with our own that uses ProcessHelper.GetCurrentProcessVersion()
            // for consistent output with "gvfs version" and AOT compatibility.
            foreach (Option opt in rootCommand.Options)
            {
                if (opt.Name == "--version")
                {
                    rootCommand.Options.Remove(opt);
                    break;
                }
            }

            Option<bool> versionOption = new Option<bool>("--version", "-v") { Description = "Display the GVFS version" };
            rootCommand.Add(versionOption);
            rootCommand.SetAction((ParseResult result) =>
            {
                if (result.GetValue(versionOption))
                {
                    Console.WriteLine("GVFS " + ProcessHelper.GetCurrentProcessVersion());
                }
                else
                {
                    // No args — show help
                    rootCommand.Parse(new[] { "--help" }).Invoke();
                }
            });

            rootCommand.Add(CacheServerVerb.CreateCommand());
            rootCommand.Add(CacheVerb.CreateCommand());
            rootCommand.Add(CloneVerb.CreateCommand());
            rootCommand.Add(ConfigVerb.CreateCommand());
            rootCommand.Add(DehydrateVerb.CreateCommand());
            rootCommand.Add(DiagnoseVerb.CreateCommand());
            rootCommand.Add(HealthVerb.CreateCommand());
            rootCommand.Add(LogVerb.CreateCommand());
            rootCommand.Add(MountVerb.CreateCommand());
            rootCommand.Add(PrefetchVerb.CreateCommand());
            rootCommand.Add(RepairVerb.CreateCommand());
            rootCommand.Add(ServiceVerb.CreateCommand());
            rootCommand.Add(SparseVerb.CreateCommand());
            rootCommand.Add(StatusVerb.CreateCommand());
            rootCommand.Add(UnmountVerb.CreateCommand());
            rootCommand.Add(UpgradeVerb.CreateCommand());

            Command versionCmd = new Command("version", "Display the GVFS version");
            versionCmd.SetAction((ParseResult result) =>
            {
                Console.WriteLine("GVFS " + ProcessHelper.GetCurrentProcessVersion());
            });
            rootCommand.Add(versionCmd);

            // Explicit "help" subcommand for backward compatibility.
            // System.CommandLine handles --help/-h/-? automatically, but the old
            // CommandLineParser also accepted "gvfs help" as a bare subcommand.
            Command helpCmd = new Command("help", "Display help information");
            Argument<string> helpSubcommandArg = new Argument<string>("subcommand")
            {
                Description = "The subcommand to get help for",
                Arity = ArgumentArity.ZeroOrOne,
            };
            helpSubcommandArg.DefaultValueFactory = (_) => "";
            helpCmd.Add(helpSubcommandArg);
            helpCmd.SetAction((ParseResult result) =>
            {
                string subcommand = result.GetValue(helpSubcommandArg) ?? "";
                if (!string.IsNullOrEmpty(subcommand))
                {
                    rootCommand.Parse(new[] { subcommand, "--help" }).Invoke();
                }
                else
                {
                    rootCommand.Parse(new[] { "--help" }).Invoke();
                }
            });
            rootCommand.Add(helpCmd);

            return rootCommand;
        }
    }
}
