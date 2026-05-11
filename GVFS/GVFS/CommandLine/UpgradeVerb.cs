using GVFS.Common;
using System;

namespace GVFS.CommandLine
{
    public class UpgradeVerb : GVFSVerb.ForNoEnlistment
    {
        private const string UpgradeVerbName = "upgrade";

        public UpgradeVerb()
        {
            this.Output = Console.Out;
        }

        public bool Confirmed { get; set; }

        public bool DryRun { get; set; }

        public bool NoVerify { get; set; }

        public static System.CommandLine.Command CreateCommand()
        {
            System.CommandLine.Command cmd = new System.CommandLine.Command("upgrade", "Checks for new GVFS release, downloads and installs it when available.");

            System.CommandLine.Option<bool> confirmOption = new System.CommandLine.Option<bool>("--confirm") { Description = "Pass in this flag to actually install the newest release" };
            cmd.Add(confirmOption);

            System.CommandLine.Option<bool> dryRunOption = new System.CommandLine.Option<bool>("--dry-run") { Description = "Display progress and errors, but don't install GVFS" };
            cmd.Add(dryRunOption);

            System.CommandLine.Option<bool> noVerifyOption = new System.CommandLine.Option<bool>("--no-verify") { Description = "Do not verify NuGet packages after downloading them. Some platforms do not support NuGet verification." };
            cmd.Add(noVerifyOption);

            System.CommandLine.Option<string> internalOption = GVFSVerb.CreateInternalParametersOption();
            cmd.Add(internalOption);

            GVFSVerb.SetActionForNoEnlistment<UpgradeVerb>(cmd, internalOption,
                (verb, result) =>
                {
                    verb.Confirmed = result.GetValue(confirmOption);
                    verb.DryRun = result.GetValue(dryRunOption);
                    verb.NoVerify = result.GetValue(noVerifyOption);
                });

            return cmd;
        }

        protected override string VerbName
        {
            get { return UpgradeVerbName; }
        }

        public override void Execute()
        {
            Console.Error.WriteLine("'gvfs upgrade' is no longer supported. Visit https://github.com/microsoft/vfsforgit for the latest install/upgrade instructions.");
        }
    }
}
