using System;
using System.IO;

namespace GVFS.UnitTests.Mock.Git
{
    // This repository requires no git.exe. The non-ASCII path checks libgit2's UTF-8 marshalling.
    internal sealed class RetryConfigTestRepository : IDisposable
    {
        private readonly IsolatedLibGit2Config machineConfig = new IsolatedLibGit2Config();

        public RetryConfigTestRepository(string extraConfig)
        {
            this.WorkingDirectory = Path.Combine(Path.GetTempPath(), "RetryConfigTests_ü_" + Guid.NewGuid().ToString("N"));
            string dotGit = Path.Combine(this.WorkingDirectory, ".git");
            Directory.CreateDirectory(Path.Combine(dotGit, "objects"));
            Directory.CreateDirectory(Path.Combine(dotGit, "refs", "heads"));
            File.WriteAllText(Path.Combine(dotGit, "HEAD"), "ref: refs/heads/main\n");
            File.WriteAllText(
                Path.Combine(dotGit, "config"),
                "[core]\n\trepositoryformatversion = 0\n\tbare = false\n" + extraConfig);
        }

        public string WorkingDirectory { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(this.WorkingDirectory, recursive: true);
            }
            finally
            {
                this.machineConfig.Dispose();
            }
        }
    }
}
