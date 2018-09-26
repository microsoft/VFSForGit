using CommandLine;
using GVFS.Common;
using System;

namespace GVFS.CommandLine
{
    [Verb(ConfigVerbName, HelpText = "Set and Get GVFS config settings.")]
    public class ConfigVerb : GVFSVerb
    {
        private const string ConfigVerbName = "config";
        private LocalGVFSConfig localConfig;

        public override string EnlistmentRootPathParameter { get; set; }

        protected override string VerbName
        {
            get { return ConfigVerbName; }
        }

        public override void Execute()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length < 3 || args.Length > 4)
            {
                string usageString = string.Join(
                    Environment.NewLine,
                    "Error: wrong number of arguments.",
                    "Usage: gvfs config <config> <value>");
                this.ReportErrorAndExit(usageString);
            }

            this.localConfig = new LocalGVFSConfig(GVFSPlatform.Instance.GitInstallation.GetInstalledGitBinPath());

            string key = args[2];
            string value = null;
            string error = null;
            bool isRead = args.Length == 3;

            key = args[2];
            if (isRead)
            {
                if (this.localConfig.TryGetValueForKey(key, out value, out error))
                {
                    Console.WriteLine(value);
                }
                else
                {
                    this.ReportErrorAndExit(error);
                }
            }
            else
            {
                value = args[3];
                if (!this.localConfig.TrySetValueForKey(key, value, out error))
                {
                    this.ReportErrorAndExit(error);
                }
            }
        }
    }
}
