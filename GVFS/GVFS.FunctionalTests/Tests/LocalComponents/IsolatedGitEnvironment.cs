using System;
using System.Collections.Generic;
using System.IO;

namespace GVFS.FunctionalTests.Tests.LocalComponents
{
    // Callers must be non-parallel because Git inherits these process-wide environment variables.
    internal sealed class IsolatedGitEnvironment : IDisposable
    {
        private readonly Dictionary<string, string> originalValues = new Dictionary<string, string>();

        public IsolatedGitEnvironment()
        {
            this.RootDirectory = Path.Combine(Path.GetTempPath(), "LocalComponents_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(this.RootDirectory);

            foreach (string variable in new[]
            {
                "HOME", "XDG_CONFIG_HOME", "GIT_CONFIG", "GIT_CONFIG_COUNT", "GIT_CONFIG_GLOBAL",
                "GIT_CONFIG_NOSYSTEM", "GIT_CONFIG_PARAMETERS", "GIT_CONFIG_SYSTEM",
                "GIT_DIR", "GIT_COMMON_DIR", "GIT_WORK_TREE",
            })
            {
                this.originalValues.Add(variable, Environment.GetEnvironmentVariable(variable));
                Environment.SetEnvironmentVariable(variable, null);
            }

            Environment.SetEnvironmentVariable("HOME", this.RootDirectory);
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(this.RootDirectory, "xdg"));
            Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");
        }

        public string RootDirectory { get; }

        public void Dispose()
        {
            foreach (KeyValuePair<string, string> variable in this.originalValues)
            {
                Environment.SetEnvironmentVariable(variable.Key, variable.Value);
            }

            Directory.Delete(this.RootDirectory, recursive: true);
        }
    }
}
