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
            get { return GVFSConstants.DotGit.Config; }
        }

        public override IssueType HasIssue(List<string> messages)
        {
            GitProcess git = new GitProcess(this.Enlistment);

            // A SHA256 repository is not something 'gvfs repair' can make usable (see
            // TryFixIssues), so report it as an unfixable issue rather than letting
            // 'gvfs diagnose'/'gvfs repair' declare the enlistment healthy. Unlike ref
            // storage format, SHA256 has no physical on-disk marker independent of git
            // config - extensions.objectformat is the only signal - so a config-read
            // failure here is not itself flagged; it falls through to the normal config
            // checks below, which is exactly what repair is meant to diagnose.
            if (ObjectFormat.IsSha256Repo(git))
            {
                messages.Add(ObjectFormat.UnsupportedSha256ErrorMessage);
                return IssueType.CantFix;
            }

            GitProcess.ConfigResult originResult = git.GetOriginUrl();
            string error;
            string originUrl;
            if (!originResult.TryParseAsString(out originUrl, out error))
            {
                if (error.Contains("--local"))
                {
                    // example error: '--local can only be used inside a git repository'
                    // Corrupting the git config does not cause git to not recognize the current folder as "not a git repository".
                    // This is a symptom of deeper issues such as missing HEAD file or refs folders.
                    messages.Add("An issue was found that may be a side-effect of other issues. Fix them with 'gvfs repair --confirm' then 'gvfs repair' again.");
                    return IssueType.CantFix;
                }

                messages.Add("Could not read origin url: " + error);
                return IssueType.Fixable;
            }

            if (originUrl == null)
            {
                messages.Add("Remote 'origin' is not configured for this repo. You can fix this by running 'git remote add origin <repourl>'");
                return IssueType.CantFix;
            }

            // We've validated the repo URL, so now make sure we can authenticate
            try
            {
                GVFSEnlistment enlistment = GVFSEnlistment.CreateFromDirectory(
                    this.Enlistment.PrimaryEnlistmentRoot,
                    this.Enlistment.GitBinPath,
                    authentication: null);

                string authError;
                if (!enlistment.Authentication.TryInitialize(this.Tracer, enlistment, out authError))
                {
                    messages.Add("Authentication failed. Run 'gvfs log' for more info.");
                    messages.Add($"{GVFSConstants.DotGit.Config} is valid and remote 'origin' is set, but may have a typo:");
                    messages.Add(originUrl.Trim());
                    return IssueType.CantFix;
                }
            }
            catch (InvalidRepoException)
            {
                messages.Add("An issue was found that may be a side-effect of other issues. Fix them with 'gvfs repair --confirm' then 'gvfs repair' again.");
                return IssueType.CantFix;
            }

            return IssueType.None;
        }

        public override FixResult TryFixIssues(List<string> messages)
        {
            // Check before touching the config file at all: TryFixIssues rebuilds the
            // entire config from scratch (wiping it to empty, then writing only the
            // required/optional settings this codebase knows about via
            // TrySetRequiredGitConfigSettings, which force-writes
            // core.repositoryformatversion=0 and drops extensions.objectformat).
            // Rebuilding a SHA256 repo's config that way would strip the objectformat
            // extension while its objects/index remain SHA256-hashed, producing a
            // differently-broken repo. There is no way for 'gvfs repair' to make a
            // SHA256 repo usable by VFS for Git, so fail clearly instead of attempting
            // the rebuild.
            //
            // Unlike ref storage format, SHA256 has no physical on-disk marker
            // independent of git config - extensions.objectformat is the only signal -
            // so a config-read failure here is NOT blocked, unlike at mount/clone/
            // FastFetch: repair's whole purpose is to rebuild a corrupt config, so a
            // repo whose config is merely unreadable is exactly what repair must be
            // allowed to fix. This is an inherent limitation: a SHA256 repo whose
            // config is ALSO corrupt cannot be distinguished from an ordinary corrupt
            // SHA1 repo here, and would be silently rebuilt as SHA1.
            if (!ObjectFormat.TryIsSha256Repo(new GitProcess(this.Enlistment), out bool isSha256Repo, out string objectFormatReadError))
            {
                this.Tracer.RelatedWarning("Could not determine the repository's object format; proceeding with repair: " + objectFormatReadError);
            }
            else if (isSha256Repo)
            {
                messages.Add(ObjectFormat.UnsupportedSha256ErrorMessage);
                return FixResult.Failure;
            }

            string configPath = Path.Combine(this.Enlistment.WorkingDirectoryBackingRoot, GVFSConstants.DotGit.Config);
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
                messages.Add($"Unable to create default {GVFSConstants.DotGit.Config}.");
                this.RestoreFromBackupFile(configBackupPath, configPath, messages);

                return FixResult.Failure;
            }

            // Don't output the validation output unless it turns out we couldn't fix the problem
            List<string> validationMessages = new List<string>();

            // HasIssue should return CantFix because we can't set the repo url ourselves,
            // but getting Fixable means that we still failed
            if (this.HasIssue(validationMessages) == IssueType.Fixable)
            {
                messages.Add($"Reinitializing the {GVFSConstants.DotGit.Config} did not fix the issue. Check the errors below for more details:");
                messages.AddRange(validationMessages);

                this.RestoreFromBackupFile(configBackupPath, configPath, messages);

                return FixResult.Failure;
            }

            if (!this.TryDeleteFile(configBackupPath))
            {
                messages.Add($"Failed to delete {GVFSConstants.DotGit.Config} backup file: " + configBackupPath);
            }

            messages.Add($"Reinitialized {GVFSConstants.DotGit.Config}. You will need to manually add the origin remote by running");
            messages.Add("git remote add origin <repo url>");
            messages.Add("If you previously configured a custom cache server, you will need to configure it again.");

            return FixResult.ManualStepsRequired;
        }
    }
}
