using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO;

namespace GVFS.Common
{
    public class LocalGVFSConfig
    {
        private const string FileName = "gvfs.config";
        private readonly string configFile;
        private readonly PhysicalFileSystem fileSystem;
        private FileBasedDictionary<string, string> allSettings;
        
        public LocalGVFSConfig()
        {
            string servicePath = Paths.GetServiceDataRoot(GVFSConstants.Service.ServiceName);
            string gvfsDirectory = Path.GetDirectoryName(servicePath);

            this.configFile = Path.Combine(gvfsDirectory, FileName);
            this.fileSystem = new PhysicalFileSystem();
        }

        private delegate bool ConfigAction();

        public bool TryGetAllConfig(out Dictionary<string, string> allConfig, out string error, ITracer tracer)
        {
            Dictionary<string, string> configCopy = null;
            if (!this.TryPerformAction(
                () =>
                {
                    this.allSettings.GetAllKeysAndValues(out configCopy);
                    return true;
                },
                tracer,
                out error))
            {
                allConfig = null;
                return false;
            }

            allConfig = configCopy;
            error = null;
            return true;
        }

        public bool TryGetConfig(
            string name, 
            out string value, 
            out string error,
            ITracer tracer)
        {
            string valueFromDict = null;
            if (!this.TryPerformAction(
                () =>
                {
                    this.allSettings.TryGetValue(name, out valueFromDict);
                    return true;
                },
                tracer,
                out error))
            {
                value = null;
                return false;
            }

            value = valueFromDict;
            return true;
        }

        public bool TrySetConfig(
            string name, 
            string value, 
            out string error,
            ITracer tracer)
        {
            if (!this.TryPerformAction(
                () => 
                {
                    this.allSettings.SetValueAndFlush(name, value);
                    return true;
                }, 
                tracer, 
                out error))
            {
                error = $"Error setting config value {name}: {value}. {error}";
                return false;
            }

            return true;
        }

        public bool TryRemoveConfig(string name, out string error, ITracer tracer)
        {
            if (!this.TryPerformAction(
                () =>
                {
                    this.allSettings.RemoveAndFlush(name);
                    return true;
                },
                tracer,
                out error))
            {
                error = $"Error removing config value {name}. {error}";
                return false;
            }

            return true;
        }

        private bool TryPerformAction(ConfigAction action, ITracer tracer, out string error)
        {
            if (!this.TryLoadSettings(tracer, out error))
            {
                error = $"Error loading config settings.";
                return false;
            }

            try
            {
                if (action())
                {
                    error = null;
                    return true;
                }
            }
            catch (FileBasedCollectionException exception)
            {
                if (tracer != null)
                {
                    tracer.RelatedError(exception.ToString());
                }

                error = exception.Message;
            }

            return false;
        }

        private bool TryLoadSettings(ITracer tracer, out string error)
        {
            if (this.allSettings == null)
            {
                FileBasedDictionary<string, string> config = null;
                if (FileBasedDictionary<string, string>.TryCreate(
                    tracer: tracer,
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
