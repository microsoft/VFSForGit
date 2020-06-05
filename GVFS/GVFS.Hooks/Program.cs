using GVFS.Common;
using GVFS.Common.NamedPipes;
using GVFS.Hooks.HooksPlatform;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GVFS.Hooks
{
    public class Program
    {
        private const string PreCommandHook = "pre-command";
        private const string PostCommandHook = "post-command";

        private const string GitPidArg = "--git-pid=";
        private const int InvalidProcessId = -1;

        private const int PostCommandSpinnerDelayMs = 500;

        private static string enlistmentRoot;
        private static string enlistmentPipename;
        private static Random random = new Random();

        private delegate void LockRequestDelegate(bool unattended, string[] args, int pid, NamedPipeClient pipeClient);

        public static void Main(string[] args)
        {
            try
            {
                if (args.Length < 2)
                {
                    ExitWithError("Usage: gvfs.hooks.exe --git-pid=<pid> <hook> <git verb> [<other arguments>]");
                }

                bool unattended = GVFSEnlistment.IsUnattended(tracer: null);

                string errorMessage;
                string normalizedCurrentDirectory;
                if (!GVFSHooksPlatform.TryGetNormalizedPath(Environment.CurrentDirectory, out normalizedCurrentDirectory, out errorMessage))
                {
                    ExitWithError($"Failed to determine final path for current directory {Environment.CurrentDirectory}. Error: {errorMessage}");
                }

                if (!GVFSHooksPlatform.TryGetGVFSEnlistmentRoot(Environment.CurrentDirectory, out enlistmentRoot, out errorMessage))
                {
                    // Nothing to hook when being run outside of a GVFS repo.
                    // This is also the path when run with --git-dir outside of a GVFS directory, see Story #949665
                    Environment.Exit(0);
                }

                enlistmentPipename = GVFSHooksPlatform.GetNamedPipeName(enlistmentRoot);

                switch (GetHookType(args))
                {
                    case PreCommandHook:
                        CheckForLegalCommands(args);
                        RunLockRequest(args, unattended, AcquireGVFSLockForProcess);
                        RunPreCommands(args);
                        break;

                    case PostCommandHook:
                        // Do not release the lock if this request was only run to see if it could acquire the GVFSLock,
                        // but did not actually acquire it.
                        if (!CheckGVFSLockAvailabilityOnly(args))
                        {
                            RunLockRequest(args, unattended, ReleaseGVFSLock);
                        }

                        RunPostCommands(args, unattended);
                        break;

                    default:
                        ExitWithError("Unrecognized hook: " + string.Join(" ", args));
                        break;
                }
            }
            catch (Exception ex)
            {
                ExitWithError("Unexpected exception: " + ex.ToString());
            }
        }

        private static void RunPreCommands(string[] args)
        {
            string command = GetGitCommand(args);
            switch (command)
            {
                case "fetch":
                case "pull":
                    ProcessHelper.Run("gvfs", "prefetch --commits", redirectOutput: false);
                    break;
            }
        }

        private static void RunPostCommands(string[] args, bool unattended)
        {
            if (!unattended)
            {
                RemindUpgradeAvailable();
            }
        }

        private static void RemindUpgradeAvailable()
        {
            // The idea is to generate a random number between 0 and 100. To make
            // sure that the reminder is displayed only 10% of the times a git
            // command is run, check that the random number is between 0 and 10,
            // which will have a probability of 10/100 == 10%.
            int reminderFrequency = 10;
            int randomValue = random.Next(0, 100);

            if ((IsUpgradeMessageDeterministic() || randomValue <= reminderFrequency) &&
                ProductUpgraderInfo.IsLocalUpgradeAvailable(tracer: null, highestAvailableVersionDirectory: GVFSHooksPlatform.GetUpgradeHighestAvailableVersionDirectory()))
            {
                Console.WriteLine(Environment.NewLine + GVFSHooksPlatform.GetUpgradeReminderNotification());
            }
        }

        private static void ExitWithError(params string[] messages)
        {
            foreach (string message in messages)
            {
                Console.WriteLine(message);
            }

            Environment.Exit(1);
        }

        private static void CheckForLegalCommands(string[] args)
        {
            string command = GetGitCommand(args);
            switch (command)
            {
                case "gui":
                    ExitWithError(GVFSHooksPlatform.GetGitGuiBlockedMessage());
                    break;
            }
        }

        private static void RunLockRequest(string[] args, bool unattended, LockRequestDelegate requestToRun)
        {
            try
            {
                if (ShouldLock(args))
                {
                    using (NamedPipeClient pipeClient = new NamedPipeClient(enlistmentPipename))
                    {
                        if (!pipeClient.Connect())
                        {
                            ExitWithError("The repo does not appear to be mounted. Use 'gvfs status' to check.");
                        }

                        int pid = GetParentPid(args);
                        if (pid == Program.InvalidProcessId ||
                            !GVFSHooksPlatform.IsProcessActive(pid))
                        {
                            ExitWithError("GVFS.Hooks: Unable to find parent git.exe process " + "(PID: " + pid + ").");
                        }

                        requestToRun(unattended, args, pid, pipeClient);
                    }
                }
            }
            catch (Exception exc)
            {
                ExitWithError(
                    "Unable to initialize Git command.",
                    "Ensure that GVFS is running.",
                    exc.ToString());
            }
        }

        private static string GenerateFullCommand(string[] args)
        {
            return "git " + string.Join(" ", args.Skip(1).Where(arg => !arg.StartsWith(GitPidArg)));
        }

        private static int GetParentPid(string[] args)
        {
            string pidArg = args.SingleOrDefault(x => x.StartsWith(GitPidArg));
            if (!string.IsNullOrEmpty(pidArg))
            {
                pidArg = pidArg.Remove(0, GitPidArg.Length);
                int pid;
                if (int.TryParse(pidArg, out pid))
                {
                    return pid;
                }
            }

            ExitWithError(
                "Git did not supply the process Id.",
                "Ensure you are using the correct version of the git client.");

            return Program.InvalidProcessId;
        }

        private static void AcquireGVFSLockForProcess(bool unattended, string[] args, int pid, NamedPipeClient pipeClient)
        {
            string result;
            bool checkGvfsLockAvailabilityOnly = CheckGVFSLockAvailabilityOnly(args);
            string fullCommand = GenerateFullCommand(args);
            string gitCommandSessionId = GetGitCommandSessionId();

            if (!GVFSLock.TryAcquireGVFSLockForProcess(
                    unattended,
                    pipeClient,
                    fullCommand,
                    pid,
                    GVFSHooksPlatform.IsElevated(),
                    isConsoleOutputRedirectedToFile: GVFSHooksPlatform.IsConsoleOutputRedirectedToFile(),
                    checkAvailabilityOnly: checkGvfsLockAvailabilityOnly,
                    gvfsEnlistmentRoot: null,
                    gitCommandSessionId: gitCommandSessionId,
                    result: out result))
            {
                ExitWithError(result);
            }
        }

        private static void ReleaseGVFSLock(bool unattended, string[] args, int pid, NamedPipeClient pipeClient)
        {
            string fullCommand = GenerateFullCommand(args);

            GVFSLock.ReleaseGVFSLock(
                unattended,
                pipeClient,
                fullCommand,
                pid,
                GVFSHooksPlatform.IsElevated(),
                GVFSHooksPlatform.IsConsoleOutputRedirectedToFile(),
                response =>
                {
                    if (response == null || response.ResponseData == null)
                    {
                        Console.WriteLine("\nError communicating with GVFS: Run 'gvfs status' to check the status of your repo");
                    }
                    else if (response.ResponseData.HasFailures)
                    {
                        if (response.ResponseData.FailureCountExceedsMaxFileNames)
                        {
                            Console.WriteLine(
                                "\nGVFS failed to update {0} files, run 'git status' to check the status of files in the repo",
                                response.ResponseData.FailedToDeleteCount + response.ResponseData.FailedToUpdateCount);
                        }
                        else
                        {
                            string deleteFailuresMessage = BuildUpdatePlaceholderFailureMessage(response.ResponseData.FailedToDeleteFileList, "delete", "git clean -f ");
                            if (deleteFailuresMessage.Length > 0)
                            {
                                Console.WriteLine(deleteFailuresMessage);
                            }

                            string updateFailuresMessage = BuildUpdatePlaceholderFailureMessage(response.ResponseData.FailedToUpdateFileList, "update", "git checkout -- ");
                            if (updateFailuresMessage.Length > 0)
                            {
                                Console.WriteLine(updateFailuresMessage);
                            }
                        }
                    }
                },
                gvfsEnlistmentRoot: null,
                waitingMessage: "Waiting for GVFS to parse index and update placeholder files",
                spinnerDelay: PostCommandSpinnerDelayMs);
        }

        private static bool CheckGVFSLockAvailabilityOnly(string[] args)
        {
            try
            {
                // Don't acquire the GVFS lock if the git command is not acquiring locks.
                // This enables tools to run status commands without to the index and
                // blocking other commands from running. The git argument
                // "--no-optional-locks" results in a 'negative'
                // value GIT_OPTIONAL_LOCKS environment variable.
                return GetGitCommand(args).Equals("status", StringComparison.OrdinalIgnoreCase) &&
                    (args.Any(arg => arg.Equals("--no-lock-index", StringComparison.OrdinalIgnoreCase)) ||
                    IsGitEnvVarDisabled("GIT_OPTIONAL_LOCKS"));
            }
            catch (Exception e)
            {
                ExitWithError("Failed to determine if GVFS should aquire GVFS lock: " + e.ToString());
            }

            return false;
        }

        private static string BuildUpdatePlaceholderFailureMessage(List<string> fileList, string failedOperation, string recoveryCommand)
        {
            if (fileList == null || fileList.Count == 0)
            {
                return string.Empty;
            }

            fileList.Sort(StringComparer.OrdinalIgnoreCase);
            string message = "\nGVFS was unable to " + failedOperation + " the following files. To recover, close all handles to the files and run these commands:";
            message += string.Concat(fileList.Select(x => "\n    " + recoveryCommand + x));
            return message;
        }

        private static bool IsGitEnvVarDisabled(string envVar)
        {
            string envVarValue = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrEmpty(envVarValue))
            {
                if (string.Equals(envVarValue, "false", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(envVarValue, "no", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(envVarValue, "off", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(envVarValue, "0", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ShouldLock(string[] args)
        {
            string gitCommand = GetGitCommand(args);

            switch (gitCommand)
            {
                // Keep these alphabetically sorted
                case "blame":
                case "branch":
                case "cat-file":
                case "check-attr":
                case "check-ignore":
                case "check-mailmap":
                case "commit-graph":
                case "config":
                case "credential":
                case "diff":
                case "diff-files":
                case "diff-index":
                case "diff-tree":
                case "difftool":
                case "fetch":
                case "for-each-ref":
                case "help":
                case "hash-object":
                case "index-pack":
                case "log":
                case "ls-files":
                case "ls-tree":
                case "merge-base":
                case "multi-pack-index":
                case "name-rev":
                case "pack-objects":
                case "push":
                case "remote":
                case "rev-list":
                case "rev-parse":
                case "show":
                case "show-ref":
                case "symbolic-ref":
                case "tag":
                case "unpack-objects":
                case "update-ref":
                case "version":
                case "web--browse":
                    return false;

                /*
                 * There are several git commands that are "unsupported" in virtualized (VFS4G)
                 * enlistments that are blocked by git. Usually, these are blocked before they acquire
                 * a GVFSLock, but the submodule command is different, and is blocked after acquiring the
                 * GVFS lock. This can cause issues if another action is attempting to create placeholders.
                 * As we know the submodule command is a no-op, allow it to proceed without acquiring the
                 * GVFSLock. I have filed issue #1164 to track having git block all unsupported commands
                 * before calling the pre-command hook.
                 */
                case "submodule":
                    return false;
            }

            if (gitCommand == "reset" && args.Contains("--soft"))
            {
                return false;
            }

            if (!KnownGitCommands.Contains(gitCommand) &&
                IsAlias(gitCommand))
            {
                return false;
            }

            return true;
        }

        private static string GetHookType(string[] args)
        {
            return args[0].ToLowerInvariant();
        }

        private static string GetGitCommand(string[] args)
        {
            string command = args[1].ToLowerInvariant();
            if (command.StartsWith("git-"))
            {
                command = command.Substring(4);
            }

            return command;
        }

        private static bool IsAlias(string command)
        {
            ProcessResult result = ProcessHelper.Run("git", "config --get alias." + command);

            return !string.IsNullOrEmpty(result.Output);
        }

        private static string GetGitCommandSessionId()
        {
            try
            {
                return Environment.GetEnvironmentVariable("GIT_TR2_PARENT_SID", EnvironmentVariableTarget.Process) ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static bool IsUpgradeMessageDeterministic()
        {
            try
            {
                return Environment.GetEnvironmentVariable("GVFS_UPGRADE_DETERMINISTIC", EnvironmentVariableTarget.Process) != null;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
