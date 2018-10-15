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
            'l',
            "list",
            SetName = "needNoKeys",
            Required = false,
            HelpText = "Show all settings")]
        public bool List { get; set; }

        [Option(
            'd',
            "delete",
            SetName = "needsKey",
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
            if (GVFSPlatform.Instance.IsUnderConstruction)
            {
                this.ReportErrorAndExit("`gvfs config` is not yet implemented on this operating system.");
            }

            this.localConfig = new LocalGVFSConfig();
            bool keySpecified = !string.IsNullOrEmpty(this.Key);
            bool isDelete = !string.IsNullOrEmpty(this.KeyToDelete);
            string error = null;

            if (this.List || (!keySpecified && !isDelete))
            {
                Dictionary<string, string> allSettings;
                if (!this.localConfig.TryGetAllConfig(out allSettings, out error))
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

            if (isDelete)
            {
                if (!this.localConfig.TryRemoveConfig(this.KeyToDelete, out error))
                {
                    this.ReportErrorAndExit(error);
                }

                return;
            }
                        
            if (keySpecified)
            {
                bool valueSpecified = !string.IsNullOrEmpty(this.Value);
                if (valueSpecified)
                {
                    if (!this.localConfig.TrySetConfig(this.Key, this.Value, out error))
                    {
                        this.ReportErrorAndExit(error);
                    }

                    return;
                }
                else
                {
                    string valueRead = null;
                    if (!this.localConfig.TryGetConfig(this.Key, out valueRead, out error))
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
        }
    }
}