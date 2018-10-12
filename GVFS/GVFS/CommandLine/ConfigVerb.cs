using CommandLine;
using GVFS.Common;
using System;
using System.Collections.Generic;

namespace GVFS.CommandLine
{
    [Verb(ConfigVerbName, HelpText = "Get and set GVFS options.")]
    public class ConfigVerb : GVFSVerb.ForNoEnlistment
    {
        private const string ConfigVerbName = "config";
        private LocalGVFSConfig localConfig;

        [Option(
            "list",
            SetName = "needNoKeys",
            Default = false,
            Required = false,
            HelpText = "Show all settings")]
        public bool List { get; set; }

        [Option(
            "delete",
            SetName = "needsKey",
            Default = null,
            Required = false,
            HelpText = "Delete specified setting")]
        public string KeyToDelete { get; set; }

        [Value(
            0,
            Required = false,
            MetaName = "Setting name",
            HelpText = "Name of setting that is to be set, read or deleted")]
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
            if (this.List)
            {
                Dictionary<string, string> allSettings;
                if (!this.localConfig.TryGetAllConfig(out allSettings, out error, tracer: null))
                {
                    this.ReportErrorAndExit(error);
                }

                const string ConfigOutputFormat = "{0}={1}";
                foreach (KeyValuePair<string, string> setting in allSettings)
                {
                    Console.WriteLine(ConfigOutputFormat, setting.Key, setting.Value);
                }

                return;
            }

            if (!string.IsNullOrEmpty(this.KeyToDelete))
            {
                if (!this.localConfig.TryRemoveConfig(this.KeyToDelete, out error, tracer: null))
                {
                    this.ReportErrorAndExit(error);
                }

                return;
            }

            bool keySpecified = !string.IsNullOrEmpty(this.Key);
            if (keySpecified)
            {
                bool valueSpecified = !string.IsNullOrEmpty(this.Value);
                if (valueSpecified)
                {
                    if (!this.localConfig.TrySetConfig(this.Key, this.Value, out error, tracer: null))
                    {
                        this.ReportErrorAndExit(error);
                    }

                    return;
                }
                else
                {
                    string valueRead = null;
                    if (!this.localConfig.TryGetConfig(this.Key, out valueRead, out error, tracer: null))
                    {
                        this.ReportErrorAndExit(error);
                    }
                    else if (!string.IsNullOrEmpty(valueRead))
                    {
                        Console.WriteLine(valueRead);
                    }
                    
                    return;
                }
            }

            // this.PrintUsage();
            // RFC: There was no parsing error, CommandLine Parser would have handled it already
            // if there were any. The issue happens when user types `gvfs config`.
            // Should I print Usage or exit silently.
        }
    }
}
