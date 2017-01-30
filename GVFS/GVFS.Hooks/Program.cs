using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
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
        private const string PrecommandHook = "pre-command";

        // Deprecated - keep to support old clones
        private const string PostcommandHook = "post-command";

        private const string GitLockWaitArgName = "--internal-gitlock-waittime-ms";
        private static JsonEtwTracer tracer;

        private static Dictionary<string, string> specialArgValues = new Dictionary<string, string>();

        public static void Main(string[] args)
        {
            args = ReadAndRemoveSpecialArgValues(args);

            using (tracer = new JsonEtwTracer(GVFSConstants.GVFSEtwProviderName, "GVFS.Hooks"))
            {
                tracer.WriteStartEvent(
                    null,
                    null,
                    null,
                    new EventMetadata
                    {
                        { "Args", string.Join(" ", args) },
                    });

                try
                {
                    if (args.Length < 2)
                    {
                        ExitWithError("Usage: gvfs.hooks <hook> <git verb> [<other arguments>]");
                    }

                    switch (GetHookType(args))
                    {
                        case PrecommandHook:
                            CheckForLegalCommands(args);
                            RunPreCommands(args);
                            AcquireGlobalLock(args);
                            break;

                        case PostcommandHook:
                            // no-op - keep this handling to support old clones that had the
                            // post-command hook installed.
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
                Console.Error.WriteLine(message);
            }

            tracer.RelatedError(string.Join("\r\n", messages));
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

                    break;

                case "fsck":
                case "gc":
                case "repack":
                    ExitWithError("'git " + command + "' is not supported on a GVFS repo");
                    break;

                case "submodule":
                    ExitWithError("Submodule operations are not supported on a GVFS repo");
                    break;
            }
        }

        private static void AcquireGlobalLock(string[] args)
        {
            try
            {
                if (ShouldLock(args))
                {
                    GVFSEnlistment enlistment = GVFSEnlistment.CreateFromCurrentDirectory(null, GitProcess.GetInstalledGitBinPath());
                    if (enlistment == null)
                    {
                        ExitWithError("This hook must be run from a GVFS repo");
                    }

                    if (EnlistmentIsReady(enlistment))
                    {
                        string fullCommand = "git " + string.Join(" ", args.Skip(1));
                        int pid = ProcessHelper.GetParentProcessId("git.exe");

                        Process parentProcess = null;
                        if (pid == GVFSConstants.InvalidProcessId ||
                            !ProcessHelper.TryGetProcess(pid, out parentProcess))
                        {
                            ExitWithError("GVFS.Hooks: Unable to find parent git.exe process " + "(PID: " + pid + ").");
                        }

                        using (NamedPipeClient pipeClient = new NamedPipeClient(enlistment.NamedPipeName))
                        {
                            if (!pipeClient.Connect())
                            {
                                ExitWithError("The enlistment does not appear to be mounted. Use 'gvfs status' to check.");
                            }

                            NamedPipeMessages.AcquireLock.Request request =
                                new NamedPipeMessages.AcquireLock.Request(pid, fullCommand, ProcessHelper.GetCommandLine(parentProcess));

                            NamedPipeMessages.Message requestMessage = request.CreateMessage();
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
                                int retries = 0;
                                char[] waiting = { '\u2014', '\\', '|', '/' };
                                string message = string.Empty;
                                while (true)
                                {
                                    if (response.Result == NamedPipeMessages.AcquireLock.AcceptResult)
                                    {
                                        if (!Console.IsOutputRedirected)
                                        {
                                            Console.WriteLine("\r{0}...", message);
                                        }

                                        return;
                                    }
                                    else if (response.Result == NamedPipeMessages.AcquireLock.DenyGVFSResult)
                                    {
                                        message = "Waiting for GVFS to release the lock";
                                    }
                                    else if (response.Result == NamedPipeMessages.AcquireLock.DenyGitResult)
                                    {
                                        message = string.Format("Waiting for '{0}' to release the lock", response.ResponseData.ParsedCommand);
                                    }
                                    else
                                    {
                                        ExitWithError("Error when acquiring the lock. Unrecognized response: " + response.CreateMessage());
                                        tracer.RelatedError("Unknown LockRequestResponse: " + response);
                                    }

                                    if (Console.IsOutputRedirected && retries == 0)
                                    {
                                        Console.WriteLine("{0}...", message);
                                    }
                                    else if (!Console.IsOutputRedirected)
                                    {
                                        Console.Write("\r{0}..{1}", message, waiting[retries % waiting.Length]);
                                    }

                                    Thread.Sleep(500);

                                    pipeClient.SendRequest(requestMessage);
                                    response = new NamedPipeMessages.AcquireLock.Response(pipeClient.ReadResponse());
                                    retries++;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Error", e.ToString());
                tracer.RelatedError(metadata);

                ExitWithError(
                    "Unable to initialize Git command.",
                    "Ensure that GVFS is running.");
            }
        }

        private static bool EnlistmentIsReady(GVFSEnlistment enlistment)
        {
            bool enlistmentReady = false;
            try
            {
                enlistmentReady = !enlistment.EnlistmentMutex.WaitOne(1);
                if (!enlistmentReady)
                {
                    enlistment.EnlistmentMutex.ReleaseMutex();
                }
            }
            catch (AbandonedMutexException)
            {
                enlistmentReady = false;
            }

            return enlistmentReady;
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
                case "config":
                case "credential":
                case "diff":
                case "diff-tree":
                case "for-each-ref":
                case "help":
                case "index-pack":
                case "log":
                case "ls-tree":
                case "mv":
                case "name-rev":
                case "push":
                case "remote":
                case "rev-list":
                case "rev-parse":
                case "show":
                case "unpack-objects":
                case "version":
                case "web--browse":
                    return false;
            }

            if (gitCommand == "reset" &&
                !args.Contains("--hard") &&
                !args.Contains("--merge") &&
                !args.Contains("--keep"))
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
