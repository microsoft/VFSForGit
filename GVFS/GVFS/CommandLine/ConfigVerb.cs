using GVFS.Common;
using System;
using System.Collections.Generic;

namespace GVFS.CommandLine
{
    public class ConfigVerb : GVFSVerb.ForNoEnlistment
    {
        private const string ConfigVerbName = "config";
        private LocalGVFSConfig localConfig;

        public bool List { get; set; }

        public string KeyToDelete { get; set; }

        public string Key { get; set; }

        public string Value { get; set; }

        public static System.CommandLine.Command CreateCommand()
        {
            System.CommandLine.Command cmd = new System.CommandLine.Command("config", "Get and set GVFS options.");

            System.CommandLine.Option<bool> listOption = new System.CommandLine.Option<bool>("--list", new[] { "-l" }) { Description = "Show all settings" };
            cmd.Add(listOption);

            System.CommandLine.Option<string> deleteOption = new System.CommandLine.Option<string>("--delete", new[] { "-d" }) { Description = "Name of setting to delete" };
            cmd.Add(deleteOption);

            System.CommandLine.Argument<string> keyArg = new System.CommandLine.Argument<string>("setting-name")
            {
                Description = "Name of setting that is to be set or read",
                Arity = System.CommandLine.ArgumentArity.ZeroOrOne,
                DefaultValueFactory = (_) => "",
            };
            cmd.Add(keyArg);

            System.CommandLine.Argument<string> valueArg = new System.CommandLine.Argument<string>("setting-value")
            {
                Description = "Value of setting to be set",
                Arity = System.CommandLine.ArgumentArity.ZeroOrOne,
                DefaultValueFactory = (_) => "",
            };
            cmd.Add(valueArg);

            System.CommandLine.Option<string> internalOption = GVFSVerb.CreateInternalParametersOption();
            cmd.Add(internalOption);

            cmd.SetAction((System.CommandLine.ParseResult result) =>
            {
                ConfigVerb verb = new ConfigVerb();
                verb.List = result.GetValue(listOption);
                verb.KeyToDelete = result.GetValue(deleteOption);
                verb.Key = result.GetValue(keyArg) ?? "";
                verb.Value = result.GetValue(valueArg) ?? "";

                GVFSVerb.ApplyInternalParameters(verb, result, internalOption);
                try
                {
                    verb.Execute();
                }
                catch (GVFSVerb.VerbAbortedException)
                {
                }

                Environment.Exit((int)verb.ReturnCode);
            });

            return cmd;
        }

        protected override string VerbName
        {
            get { return ConfigVerbName; }
        }

        public override void Execute()
        {
            if (!GVFSPlatform.Instance.UnderConstruction.SupportsGVFSConfig)
            {
                this.ReportErrorAndExit("`gvfs config` is not yet implemented on this operating system.");
            }

            this.localConfig = new LocalGVFSConfig();
            string error = null;

            if (this.IsMutuallyExclusiveOptionsSet(out error))
            {
                this.ReportErrorAndExit(error);
            }

            if (this.List)
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
            }
            else if (!string.IsNullOrEmpty(this.KeyToDelete))
            {
                if (!GVFSPlatform.Instance.IsElevated())
                {
                    this.ReportErrorAndExit("`gvfs config` must be run from an elevated command prompt when deleting settings.");
                }

                if (!this.localConfig.TryRemoveConfig(this.KeyToDelete, out error))
                {
                    this.ReportErrorAndExit(error);
                }
            }
            else if (!string.IsNullOrEmpty(this.Key))
            {
                bool valueSpecified = !string.IsNullOrEmpty(this.Value);
                if (valueSpecified)
                {
                    if (!GVFSPlatform.Instance.IsElevated())
                    {
                        this.ReportErrorAndExit("`gvfs config` must be run from an elevated command prompt when configuring settings.");
                    }

                    if (!this.localConfig.TrySetConfig(this.Key, this.Value, out error))
                    {
                        this.ReportErrorAndExit(error);
                    }
                }
                else
                {
                    string valueRead = null;
                    if (!this.localConfig.TryGetConfig(this.Key, out valueRead, out error) ||
                        string.IsNullOrEmpty(valueRead))
                    {
                        this.ReportErrorAndExit(error);
                    }
                    else
                    {
                        Console.WriteLine(valueRead);
                    }
                }
            }
            else
            {
                this.ReportErrorAndExit("You must specify an option. Run `gvfs config --help` for details.");
            }
        }

        private bool IsMutuallyExclusiveOptionsSet(out string consoleMessage)
        {
            bool deleteSpecified = !string.IsNullOrEmpty(this.KeyToDelete);
            bool setOrReadSpecified = !string.IsNullOrEmpty(this.Key);
            bool listSpecified = this.List;

            if (deleteSpecified && listSpecified)
            {
                consoleMessage = "You cannot delete and list settings at the same time.";
                return true;
            }

            if (setOrReadSpecified && listSpecified)
            {
                consoleMessage = "You cannot list all and view (or update) individual settings at the same time.";
                return true;
            }

            if (setOrReadSpecified && deleteSpecified)
            {
                consoleMessage = "You cannot delete a setting and view (or update) individual settings at the same time.";
                return true;
            }

            consoleMessage = null;
            return false;
        }
    }
}