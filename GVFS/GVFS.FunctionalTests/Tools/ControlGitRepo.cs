using System;
using System.IO;

namespace GVFS.FunctionalTests.Tools
{
    public class ControlGitRepo
    {
        static ControlGitRepo()
        {
            if (!Directory.Exists(CachePath))
            {
                GitProcess.Invoke(Environment.SystemDirectory, "clone " + GVFSTestConfig.RepoToClone + " " + CachePath + " --bare");
            }
            else
            {
                GitProcess.Invoke(CachePath, "fetch origin +refs/*:refs/*");
            }
        }

        private ControlGitRepo(string repoUrl, string rootPath, string commitish)
        {
            this.RootPath = rootPath;
            this.RepoUrl = repoUrl;
            this.Commitish = commitish;
        }

        public string RootPath { get; private set; }
        public string RepoUrl { get; private set; }
        public string Commitish { get; private set; }

        private static string CachePath
        {
            get { return Path.Combine(Properties.Settings.Default.ControlGitRepoRoot, "cache"); }
        }

        public static ControlGitRepo Create(string commitish = null)
        {
            string clonePath = Path.Combine(Properties.Settings.Default.ControlGitRepoRoot, Guid.NewGuid().ToString("N"));
            return new ControlGitRepo(
                GVFSTestConfig.RepoToClone,
                clonePath,
                commitish == null ? Properties.Settings.Default.Commitish : commitish);
        }

        //
        // IMPORTANT! These must parallel the settings in GVFSVerb:TrySetRequiredGitConfigSettings
        //
        public void Initialize()
        {
            Directory.CreateDirectory(this.RootPath);
            GitProcess.Invoke(this.RootPath, "init");
            GitProcess.Invoke(this.RootPath, "config core.autocrlf false");
            GitProcess.Invoke(this.RootPath, "config core.editor true");
            GitProcess.Invoke(this.RootPath, "config merge.stat false");
            GitProcess.Invoke(this.RootPath, "config merge.renames false");
            GitProcess.Invoke(this.RootPath, "config advice.statusUoption false");
            GitProcess.Invoke(this.RootPath, "config core.abbrev 40");
            GitProcess.Invoke(this.RootPath, "config core.useBuiltinFSMonitor false");
            GitProcess.Invoke(this.RootPath, "config pack.useSparse true");
            GitProcess.Invoke(this.RootPath, "config reset.quiet true");
            GitProcess.Invoke(this.RootPath, "config status.aheadbehind false");
            GitProcess.Invoke(this.RootPath, "config user.name \"Functional Test User\"");
            GitProcess.Invoke(this.RootPath, "config user.email \"functional@test.com\"");
            GitProcess.Invoke(this.RootPath, "remote add origin " + CachePath);
            this.Fetch(this.Commitish);
            GitProcess.Invoke(this.RootPath, "branch --set-upstream " + this.Commitish + " origin/" + this.Commitish);
            GitProcess.Invoke(this.RootPath, "checkout " + this.Commitish);
            GitProcess.Invoke(this.RootPath, "branch --unset-upstream");

            // Enable the ORT merge strategy
            GitProcess.Invoke(this.RootPath, "config pull.twohead ort");
        }

        public void Fetch(string commitish)
        {
            GitProcess.Invoke(this.RootPath, "fetch origin " + commitish);
        }
    }
}
