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

            // A reftable repository is not something 'gvfs repair' can make usable (see
            // TryFixIssues), so report it as an unfixable issue rather than letting
            // 'gvfs diagnose'/'gvfs repair' declare the enlistment healthy. Detect it both
            // from the config extension and from the physical .git/reftable/ directory: the
            // latter still identifies a reftable repo when the config is too corrupt for
            // 'git config' to read, which is exactly the state repair runs in. A config-read
            // failure alone is otherwise swallowed as "not reftable" so the normal config
            // checks below still run - repair exists to diagnose exactly that kind of broken
            // config.
            if (RefStorage.IsReftableRepo(git) || this.ReftableBackendDirectoryExists())
            {
                messages.Add(RefStorage.UnsupportedReftableErrorMessage);
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
            // core.repositoryformatversion=0 and drops extensions.refstorage). Rebuilding
            // a reftable repo's config that way would strip the reftable extension while
            // its refs still live in the reftable backend, producing a differently-broken
            // repo. There is no way for 'gvfs repair' to make a reftable repo usable by
            // VFS for Git, so fail clearly instead of attempting the rebuild.
            //
            // Detect reftable from the config AND from the physical .git/reftable/ directory.
            // The directory check is what makes this safe when the config is unreadable: a
            // corrupt reftable repo would otherwise fall through to the rebuild below and be
            // silently converted to a files-format config while its refs remain in reftable
            // storage. A config-read failure with no reftable directory is NOT blocked,
            // unlike at mount/clone/FastFetch: repair's whole purpose is to rebuild a corrupt
            // files-format config, so a repo whose config is merely unreadable is exactly what
            // repair must be allowed to fix.
            bool reftableDirectoryExists = this.ReftableBackendDirectoryExists();
            bool configReadable = RefStorage.TryIsReftableRepo(new GitProcess(this.Enlistment), out bool isReftableByConfig, out string refStorageReadError);
            if (reftableDirectoryExists || (configReadable && isReftableByConfig))
            {
                messages.Add(RefStorage.UnsupportedReftableErrorMessage);
                return FixResult.Failure;
            }

            if (!configReadable)
            {
                this.Tracer.RelatedWarning("Could not determine the repository's ref storage format; proceeding with repair: " + refStorageReadError);
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

        private bool ReftableBackendDirectoryExists()
        {
            // DotGitRoot assumes .git is a directory (WorkingDirectoryBackingRoot\.git), which
            // is how the whole Enlistment abstraction models it. That holds here because
            // 'gvfs repair' targets the primary enlistment, whose .git is always a real
            // directory - not a linked git worktree, where .git is a file. If repair is ever
            // taught to run against a linked worktree, this directory probe would need to
            // resolve the gitdir from that .git file first.
            string reftableDirectory = Path.Combine(this.Enlistment.DotGitRoot, RefStorage.ReftableDirectoryName);

            return Directory.Exists(reftableDirectory);
        }
    }
}
