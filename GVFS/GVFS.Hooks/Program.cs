using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.NamedPipes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace GVFS.Hooks
{
    public class Program
    {
        private const string PreCommandHook = "pre-command";
        private const string PostCommandHook = "post-command";

        private const string GitLockWaitArgName = "--internal-gitlock-waittime-ms";

        private static Dictionary<string, string> specialArgValues = new Dictionary<string, string>();
        private static string enlistmentRoot;
        private static string enlistmentPipename;

        private delegate void LockRequestDelegate(string fullcommand, int pid, Process parentProcess, NamedPipeClient pipeClient);

        public static void Main(string[] args)
        {
            args = ReadAndRemoveSpecialArgValues(args);

            try
            {
                if (args.Length < 2)
                {
                    ExitWithError("Usage: gvfs.hooks <hook> <git verb> [<other arguments>]");
                }

                enlistmentRoot = EnlistmentUtils.GetEnlistmentRoot(Environment.CurrentDirectory);
                if (string.IsNullOrEmpty(enlistmentRoot))
                {
                    // Nothing to hook when being run outside of a GVFS repo.
                    // This is also the path when run with --git-dir outside of a GVFS directory, see Story #949665
                    Environment.Exit(0);
                }

                enlistmentPipename = EnlistmentUtils.GetNamedPipeName(enlistmentRoot);

                switch (GetHookType(args))
                {
                    case PreCommandHook:
                        CheckForLegalCommands(args);
                        RunLockRequest(args, AcquireGVFSLockForProcess);
                        RunPreCommands(args);
                        break;

                    case PostCommandHook:
                        RunLockRequest(args, ReleaseGVFSLock);
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

        private static string[] ReadAndRemoveSpecialArgValues(string[] args)
        {
            string waitArgValue;
            if (TryRemoveArg(ref args, GitLockWaitArgName, out waitArgValue))
            {
                specialArgValues.Add(GitLockWaitArgName, waitArgValue);
            }

            return args;
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
                case "update-index":
                    if (ContainsArg(args, "--split-index") ||
                        ContainsArg(args, "--no-split-index"))
                    {
                        ExitWithError("Split index is not supported on a GVFS repo");
                    }

                    if (ContainsArg(args, "--index-version"))
                    {
                        ExitWithError("Changing the index version is not supported on a GVFS repo");
                    }

                    if (ContainsArg(args, "--skip-worktree") || 
                        ContainsArg(args, "--no-skip-worktree"))
                    {
                        ExitWithError("Modifying the skip worktree bit is not supported on a GVFS repo");
                    }

                    break;

                case "fsck":
                case "gc":
                case "prune":
                case "repack":
                    ExitWithError("'git " + command + "' is not supported on a GVFS repo");
                    break;

                case "submodule":
                    ExitWithError("Submodule operations are not supported on a GVFS repo");
                    break;

                case "status":
                    VerifyRenameDetectionSettings(args);
                    break;

                case "worktree":
                    ExitWithError("Worktree operations are not supported on a GVFS repo");
                    break;

                case "gui":
                    ExitWithError("To access the 'git gui' in a GVFS repo, please invoke 'git-gui.exe' instead.");
                    break;
            }
        }

        private static void VerifyRenameDetectionSettings(string[] args)
        {
            string dotGitRoot = Path.Combine(enlistmentRoot, GVFSConstants.WorkingDirectoryRootName, GVFSConstants.DotGit.Root);
            if (File.Exists(Path.Combine(dotGitRoot, GVFSConstants.MergeHeadCommitName)) ||
                File.Exists(Path.Combine(dotGitRoot, GVFSConstants.RevertHeadCommitName)))
            {
                // If no-renames and no-breaks are specified, avoid reading config.
                if (!args.Contains("--no-renames") || !args.Contains("--no-breaks"))
                {
                    Dictionary<string, GitConfigSetting> statusConfig = GitConfigHelper.GetSettings(
                        Path.Combine(dotGitRoot, "config"),
                        "status");

                    if (!IsRunningWithParamOrSetting(args, statusConfig, "--no-renames", "renames") ||
                        !IsRunningWithParamOrSetting(args, statusConfig, "--no-breaks", "breaks"))
                    {
                        ExitWithError(
                            "git status requires rename detection to be disabled during a merge or revert conflict.",
                            "Run 'git status --no-renames --no-breaks'");
                    }
                }
            }
        }

        private static bool IsRunningWithParamOrSetting(
            string[] args, 
            Dictionary<string, GitConfigSetting> configSettings, 
            string expectedArg, 
            string expectedSetting)
        {
            return 
                args.Contains(expectedArg) ||
                configSettings.ContainsKey(expectedSetting);
        }

        private static void RunLockRequest(string[] args, LockRequestDelegate requestToRun)
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

                        string fullCommand = "git " + string.Join(" ", args.Skip(1));
                        int pid = ProcessHelper.GetParentProcessId(GVFSConstants.CommandParentExecutableNames);

                        Process parentProcess = null;
                        if (pid == GVFSConstants.InvalidProcessId ||
                            !ProcessHelper.TryGetProcess(pid, out parentProcess))
                        {
                            ExitWithError("GVFS.Hooks: Unable to find parent git.exe process " + "(PID: " + pid + ").");
                        }

                        requestToRun(fullCommand, pid, parentProcess, pipeClient);
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

        private static void AcquireGVFSLockForProcess(string fullCommand, int pid, Process parentProcess, NamedPipeClient pipeClient)
        {
            NamedPipeMessages.LockRequest request =
                new NamedPipeMessages.LockRequest(pid, fullCommand);

            NamedPipeMessages.Message requestMessage = request.CreateMessage(NamedPipeMessages.AcquireLock.AcquireRequest);
            pipeClient.SendRequest(requestMessage);

            NamedPipeMessages.AcquireLock.Response response = new NamedPipeMessages.AcquireLock.Response(pipeClient.ReadResponse());

            if (response.Result == NamedPipeMessages.AcquireLock.AcceptResult)
            {
                return;
            }
            else if (response.Result == NamedPipeMessages.AcquireLock.MountNotReadyResult)
            {
                ExitWithError("GVFS has not finished initializing, please wait a few seconds and try again.");
            }
            else
            {
                string message = string.Empty;
                switch (response.Result)
                {
                    case NamedPipeMessages.AcquireLock.AcceptResult:
                        break;

                    case NamedPipeMessages.AcquireLock.DenyGVFSResult:
                        message = "Waiting for GVFS to release the lock";
                        break;

                    case NamedPipeMessages.AcquireLock.DenyGitResult:
                        message = string.Format("Waiting for '{0}' to release the lock", response.ResponseData.ParsedCommand);
                        break;

                    default:
                        ExitWithError("Error when acquiring the lock. Unrecognized response: " + response.CreateMessage());
                        break;
                }

                ConsoleHelper.ShowStatusWhileRunning(
                    () =>
                    {
                        while (response.Result != NamedPipeMessages.AcquireLock.AcceptResult)
                        {
                            Thread.Sleep(250);
                            pipeClient.SendRequest(requestMessage);
                            response = new NamedPipeMessages.AcquireLock.Response(pipeClient.ReadResponse());
                        }

                        return true;
                    },
                    message,
                    output: Console.Out,
                    showSpinner: !ConsoleHelper.IsConsoleOutputRedirectedToFile());
            }
        }

        private static void ReleaseGVFSLock(string fullCommand, int pid, Process parentProcess, NamedPipeClient pipeClient)
        {
            NamedPipeMessages.LockRequest request =
                new NamedPipeMessages.LockRequest(pid, fullCommand);

            NamedPipeMessages.Message requestMessage = request.CreateMessage(NamedPipeMessages.ReleaseLock.Request);

            pipeClient.SendRequest(requestMessage);
            pipeClient.ReadRawResponse(); // Response doesn't really matter
        }

        private static bool TryRemoveArg(ref string[] args, string argName, out string output)
        {
            output = null;
            int argIdx = Array.IndexOf(args, argName);
            if (argIdx >= 0)
            {
                if (argIdx + 1 < args.Length)
                {
                    output = args[argIdx + 1];
                    args = args.Take(argIdx).Concat(args.Skip(argIdx + 2)).ToArray();
                    return true;
                }
                else
                {
                    ExitWithError("Missing value for {0}.", argName);
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
                case "cat-file":
                case "check-attr":
                case "config":
                case "credential":
                case "diff":
                case "diff-files":
                case "diff-tree":
                case "difftool":
                case "for-each-ref":
                case "help":
                case "index-pack":
                case "log":
                case "ls-tree":
                case "merge-base":
                case "mv":
                case "name-rev":
                case "push":
                case "remote":
                case "rev-list":
                case "rev-parse":
                case "show":
                case "symbolic-ref":
                case "unpack-objects":
                case "update-ref":
                case "version":
                case "web--browse":
                    return false;
            }

            if (gitCommand == "reset" && args.Contains("--soft"))
            {
                return false;
            }

            // Don't acquire the lock if we've been explicitly asked not to. This enables tools, such as the VS Git
            // integration, to provide a "best effort" status without writing to the index. We assume that any such
            // tools will be constantly polling in the background, so missing a file once isn't a problem.
            if (gitCommand == "status" &&
                args.Contains("--no-lock-index"))
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

        private static bool ContainsArg(string[] actualArgs, string expectedArg)
        {
            return actualArgs.Contains(expectedArg, StringComparer.OrdinalIgnoreCase);
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
    }
}
