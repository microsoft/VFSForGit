using GVFS.CommandLine;
using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System.Collections.Generic;
using System.IO;

namespace GVFS.RepairJobs
{
    public class GitConfigRepairJob : RepairJob
    {
        public GitConfigRepairJob(ITracer tracer, TextWriter output, GVFSEnlistment enlistment)
            : base(tracer, output, enlistment)
        {
        }

        public override string Name
        {
            get { return ".git\\config"; }
        }

        public override IssueType HasIssue(List<string> messages)
        {
            GitProcess git = new GitProcess(this.Enlistment);
            GitProcess.Result result = git.GetOriginUrl();
            if (result.HasErrors)
            {
                if (result.Errors.Length == 0)
                {
                    messages.Add("Remote 'origin' is not configured for this repo. You can fix this by running 'git remote add origin <repourl>'");
                    return IssueType.CantFix;
                }
                else if (result.Errors.Contains("--local"))
                {
                    // example error: '--local can only be used inside a git repository'
                    // Corrupting the git config does not cause git to not recognize the current folder as "not a git repository".
                    // This is a symptom of deeper issues such as missing HEAD file or refs folders.
                    messages.Add("An issue was found that may be a side-effect of other issues. Fix them with 'gvfs repair --confirm' then 'gvfs repair' again.");
                    return IssueType.CantFix;
                }

                messages.Add("Could not read origin url: " + result.Errors);
                return IssueType.Fixable;
            }

            // At this point, we've confirmed that the repo url can be gotten, so we have to 
            // reinitialize the GitProcess with a valid repo url for 'git credential fill'
            string repoUrl = null;
            try
            {
                GVFSEnlistment enlistment = GVFSEnlistment.CreateFromDirectory(
                    this.Enlistment.EnlistmentRoot,
                    this.Enlistment.GitBinPath,
                    this.Enlistment.GVFSHooksRoot);
                git = new GitProcess(enlistment);
                repoUrl = enlistment.RepoUrl;
            }
            catch (InvalidRepoException)
            {
                messages.Add("An issue was found that may be a side-effect of other issues. Fix them with 'gvfs repair --confirm' then 'gvfs repair' again.");
                return IssueType.CantFix;
            }

            string username;
            string password;
            if (!git.TryGetCredentials(this.Tracer, repoUrl, out username, out password))
            {
                messages.Add("Authentication failed. Run 'gvfs log' for more info.");
                messages.Add(".git\\config is valid and remote 'origin' is set, but may have a typo:");
                messages.Add(result.Output.Trim());
                return IssueType.CantFix;
            }

            return IssueType.None;
        }

        public override FixResult TryFixIssues(List<string> messages)
        {
            string configPath = Path.Combine(this.Enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Config);
            string configBackupPath;
            if (!this.TryRenameToBackupFile(configPath, out configBackupPath, messages))
            {
                return FixResult.Failure;
            }
            
            File.WriteAllText(configPath, string.Empty);
            this.Tracer.RelatedInfo("Created empty file: " + configPath);

            if (!GVFSVerb.TrySetRequiredGitConfigSettings(this.Enlistment) ||
                !GVFSVerb.TrySetOptionalGitConfigSettings(this.Enlistment))
            {
                messages.Add("Unable to create default .git\\config.");
                this.RestoreFromBackupFile(configBackupPath, configPath, messages);

                return FixResult.Failure;
            }

            // Don't output the validation output unless it turns out we couldn't fix the problem
            List<string> validationMessages = new List<string>();

            // HasIssue should return CantFix because we can't set the repo url ourselves, 
            // but getting Fixable means that we still failed
            if (this.HasIssue(validationMessages) == IssueType.Fixable)
            {
                messages.Add("Reinitializing the .git\\config did not fix the issue. Check the errors below for more details:");
                messages.AddRange(validationMessages);

                this.RestoreFromBackupFile(configBackupPath, configPath, messages);

                return FixResult.Failure;
            }

            if (!this.TryDeleteFile(configBackupPath))
            {
                messages.Add("Failed to delete .git\\config backup file: " + configBackupPath);
            }

            messages.Add("Reinitialized .git\\config. You will need to manually add the origin remote by running");
            messages.Add("git remote add origin <repo url>");
            messages.Add("If you previously configured a custom cache server, you will need to configure it again.");

            return FixResult.ManualStepsRequired;
        }
    }
}
