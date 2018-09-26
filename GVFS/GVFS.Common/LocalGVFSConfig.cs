using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using System;
using System.IO;

namespace GVFS.Common
{
    public class LocalGVFSConfig
    {
        private const string FileName = "local_config.dat";
        private string configFile;
        private string gitPath;
        private PhysicalFileSystem fileSystem;

        public LocalGVFSConfig(string gitPath)
        {
            string servicePath = Paths.GetServiceDataRoot(GVFSConstants.Service.ServiceName);
            string gvfsDirectory = Path.GetDirectoryName(servicePath);

            this.configFile = Path.Combine(gvfsDirectory, FileName);
            this.gitPath = gitPath;
            this.fileSystem = new PhysicalFileSystem();
        }

        public bool TryGetValueForKey(string key, out string value, out string error)
        {
            return this.TryGetConfig(this.KeyWithGVFSSectionPrefix(key), out value, out error);
        }

        public bool TrySetValueForKey(string key, string value, out string error)
        {
            return this.TrySetConfig(this.KeyWithGVFSSectionPrefix(key), value, out error);
        }

        private bool TryGetConfig(string key, out string value, out string error)
        {
            if (!this.fileSystem.FileExists(this.configFile))
            {
                error = $"Error reading {key}. Config file({this.configFile}) does not exist.";
                value = null;
                return false;
            }

            GitProcess.Result result = GitProcess.GetFromFileConfig(this.gitPath, this.configFile, key);
            if (!result.HasErrors && !string.IsNullOrEmpty(result.Output))
            {
                error = null;
                value = result.Output.TrimEnd('\r', '\n');
                return true;
            }
            else
            {
                error = string.IsNullOrEmpty(result.Errors) ? $"Error reading \"{key}\" from file {this.configFile}." : result.Errors;
                value = null;
                return false;
            }
        }

        private bool TrySetConfig(string key, string value, out string error)
        {
            if (!this.fileSystem.FileExists(this.configFile) && !this.TryCreateConfigFile(out error))
            {
                error = $"Error setting config value {key}: {value}. {error}";
                return false;
            }

            GitProcess git = new GitProcess(this.gitPath, workingDirectoryRoot: null, gvfsHooksRoot: null);
            GitProcess.Result result = git.SetInFileConfig(this.configFile, key, value, replaceAll: true);
            if (result.HasErrors)
            {
                error = string.IsNullOrEmpty(result.Errors) ? $"Error setting config value {key}: {value}. Config file {this.configFile}." : result.Errors;
                return false;
            }

            error = null;
            return true;
        }

        private bool TryCreateConfigFile(out string error)
        {
            Exception exception = null;
            if (!this.fileSystem.TryWriteTempFileAndRename(this.configFile, string.Empty, out exception))
            {
                error = $"Could not create config file {this.configFile}. {exception.Message}";
                return false;
            }

            error = null;
            return true;
        }

        private string KeyWithGVFSSectionPrefix(string key)
        {
            const string GVFSSectionName = "gvfs";

            return string.Join(".", GVFSSectionName, key);
        }
    }
}
