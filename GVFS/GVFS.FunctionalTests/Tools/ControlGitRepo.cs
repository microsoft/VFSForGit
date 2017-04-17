using GVFS.FunctionalTests.FileSystemRunners;
using System.IO;

namespace GVFS.FunctionalTests.Tools
{
    public class ControlGitRepo
    {
        public ControlGitRepo(string repoUrl, string rootPath, string commitish)
        {
            this.RootPath = rootPath;
            this.RepoUrl = repoUrl;
            this.Commitish = commitish;
        }

        public string RootPath { get; private set; }
        public string RepoUrl { get; private set; }
        public string Commitish { get; private set; }

        public static ControlGitRepo Create()
        {
            return new ControlGitRepo(
                Properties.Settings.Default.RepoToClone, 
                Properties.Settings.Default.ControlGitRepoRoot,
                Properties.Settings.Default.Commitish);
        }

        public void Initialize()
        {
            if (Directory.Exists(this.RootPath))
            {
                this.Delete();
            }
            
            Directory.CreateDirectory(this.RootPath);
            GitProcess.Invoke(this.RootPath, "init");
            GitProcess.Invoke(this.RootPath, "config core.autocrlf false");
            GitProcess.Invoke(this.RootPath, "config merge.stat false");
            GitProcess.Invoke(this.RootPath, "config advice.statusUoption false");
            GitProcess.Invoke(this.RootPath, "config core.abbrev 40");
            GitProcess.Invoke(this.RootPath, "config user.name \"Functional Test User\"");
            GitProcess.Invoke(this.RootPath, "config user.email \"functional@test.com\"");
            GitProcess.Invoke(this.RootPath, "remote add origin " + this.RepoUrl);
            this.Fetch(this.Commitish);
            GitProcess.Invoke(this.RootPath, "branch --set-upstream " + this.Commitish + " origin/" + this.Commitish);
            GitProcess.Invoke(this.RootPath, "checkout " + this.Commitish);
            GitProcess.Invoke(this.RootPath, "branch --unset-upstream");
        }

        public void Fetch(string commitish)
        {
            GitProcess.Invoke(this.RootPath, "fetch origin " + commitish);
        }

        public void Delete()
        {
            if (Directory.Exists(this.RootPath))
            {
                SystemIORunner.RecursiveDelete(this.RootPath);
            }
        }
    }
}
