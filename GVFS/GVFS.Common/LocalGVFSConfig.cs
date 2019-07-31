using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO;

namespace GVFS.Common
{
    public class LocalGVFSConfig
    {
        public const string FileName = "gvfs.config";
        private readonly string configFile;
        private readonly PhysicalFileSystem fileSystem;
        private FileBasedDictionary<string, string> allSettings;

        public LocalGVFSConfig()
        {
            this.configFile = GVFSPlatform.Instance.GVFSConfigPath;
            this.fileSystem = new PhysicalFileSystem();
        }

        public virtual bool TryGetAllConfig(out Dictionary<string, string> allConfig, out string error)
        {
            Dictionary<string, string> configCopy = null;
            if (!this.TryPerformAction(
                () => configCopy = this.allSettings.GetAllKeysAndValues(),
                out error))
            {
                allConfig = null;
                return false;
            }

            allConfig = configCopy;
            error = null;
            return true;
        }

        public virtual bool TryGetConfig(
            string name,
            out string value,
            out string error)
        {
            string valueFromDict = null;
            if (!this.TryPerformAction(
                () => this.allSettings.TryGetValue(name, out valueFromDict),
                out error))
            {
                value = null;
                error = $"Error reading config {name}. {error}";
                return false;
            }

            value = valueFromDict;
            return true;
        }

        public virtual bool TrySetConfig(
            string name,
            string value,
            out string error)
        {
            if (!this.TryPerformAction(
                () => this.allSettings.SetValueAndFlush(name, value),
                out error))
            {
                error = $"Error writing config {name}={value}. {error}";
                return false;
            }

            return true;
        }

        public virtual bool TryRemoveConfig(string name, out string error)
        {
            if (!this.TryPerformAction(
                () => this.allSettings.RemoveAndFlush(name),
                out error))
            {
                error = $"Error deleting config {name}. {error}";
                return false;
            }

            return true;
        }

        private bool TryPerformAction(Action action, out string error)
        {
            if (!this.TryLoadSettings(out error))
            {
                error = $"Error loading config settings. {error}";
                return false;
            }

            try
            {
                action();
                error = null;
                return true;
            }
            catch (FileBasedCollectionException exception)
            {
                error = exception.Message;
            }

            return false;
        }

        private bool TryLoadSettings(out string error)
        {
            if (this.allSettings == null)
            {
                FileBasedDictionary<string, string> config = null;
                if (FileBasedDictionary<string, string>.TryCreate(
                    tracer: null,
                    dictionaryPath: this.configFile,
                    fileSystem: this.fileSystem,
                    output: out config,
                    error: out error,
                    keyComparer: StringComparer.OrdinalIgnoreCase))
                {
                    this.allSettings = config;
                    return true;
                }

                return false;
            }

            error = null;
            return true;
        }
    }
}