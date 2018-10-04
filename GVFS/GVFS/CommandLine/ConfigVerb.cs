using CommandLine;
using GVFS.Common;
using System;

namespace GVFS.CommandLine
{
    [Verb(ConfigVerbName, HelpText = "Get and set GVFS options.")]
    public class ConfigVerb : GVFSVerb.ForNoEnlistment
    {
        private const string ConfigVerbName = "config";
        private LocalGVFSConfig localConfig;

        [Value(
                0,
                Required = true,
                MetaName = "Setting name",
                HelpText = "Name of setting that is to be set or read")]
        public string Key { get; set; }

        [Value(
                1,
                Required = false,
                MetaName = "Setting value",
                HelpText = "Value of setting to be set")]
        public string Value { get; set; }

        protected override string VerbName
        {
            get { return ConfigVerbName; }
        }

        public override void Execute()
        {
            this.localConfig = new LocalGVFSConfig();

            string error = null;
            bool isRead = string.IsNullOrEmpty(this.Value);

            if (isRead)
            {
                string value = null;
                if (this.localConfig.TryGetConfig(this.Key, out value, out error, tracer: null))
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
                if (!this.localConfig.TrySetConfig(this.Key, this.Value, out error, tracer: null))
                {
                    this.ReportErrorAndExit(error);
                }
            }
        }
    }
}
