using GVFS.Common.Physical;
using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace GVFS.Common.Git
{
    public class GitProcess
    {
        private const string GitProcessName = "git.exe";
        private const string GitBinRelativePath = "cmd\\git.exe";
        private const string GitInstallationRegistryKey = "SOFTWARE\\GitForWindows";
        private const string GitInstallationRegistryInstallPathValue = "InstallPath";
        
        private object executionLock = new object();

        private Enlistment enlistment;

        public GitProcess(Enlistment enlistment)
        {
            if (enlistment == null)
            {
                throw new ArgumentNullException(nameof(enlistment));
            }
            
            if (string.IsNullOrWhiteSpace(enlistment.GitBinPath))
            {
                throw new ArgumentException(nameof(enlistment.GitBinPath));
            }

            this.enlistment = enlistment;
        }

        public static bool GitExists(string gitBinPath)
        {
            if (!string.IsNullOrWhiteSpace(gitBinPath))
            {
                return File.Exists(gitBinPath);
            }

            return ProcessHelper.WhereDirectory(GitProcessName) != null;
        }

        public static string GetInstalledGitBinPath()
        {
            string gitBinPath = RegistryUtils.GetStringFromRegistry(RegistryHive.LocalMachine, GitInstallationRegistryKey, GitInstallationRegistryInstallPathValue);
            if (!string.IsNullOrWhiteSpace(gitBinPath))
            {
                gitBinPath = Path.Combine(gitBinPath, GitBinRelativePath);
                if (File.Exists(gitBinPath))
                {
                    return gitBinPath;
                }
            }

            return null;
        }

        public static Result Init(Enlistment enlistment)
        {
            return new GitProcess(enlistment).InvokeGitOutsideEnlistment("init " + enlistment.WorkingDirectoryRoot);
        }
        
        public static bool TryGetCredentials(
            ITracer tracer,
            Enlistment enlistment,
            out string username,
            out string password)
        {
            username = null;
            password = null;

            using (ITracer activity = tracer.StartActivity("TryGetCredentials", EventLevel.Informational))
            {
                Result gitCredentialOutput = new GitProcess(enlistment).InvokeGitOutsideEnlistment(
                    "credential fill",
                    stdin => stdin.Write("url=" + enlistment.RepoUrl + "\n\n"),
                    parseStdOutLine: null);

                if (gitCredentialOutput.HasErrors)
                {
                    EventMetadata errorData = new EventMetadata();
                    errorData.Add("ErrorMessage", "Git could not get credentials: " + gitCredentialOutput.Errors);
                    tracer.RelatedError(errorData, Keywords.Network);
                    return false;
                }

                username = ParseValue(gitCredentialOutput.Output, "username=");
                password = ParseValue(gitCredentialOutput.Output, "password=");

                bool success = username != null && password != null;

                EventMetadata metadata = new EventMetadata();
                metadata.Add("Success", success);
                if (!success)
                {
                    metadata.Add("Output", gitCredentialOutput.Output);
                }
                
                activity.Stop(metadata);
                return success;
            }
        }

        public static void RevokeCredential(Enlistment enlistment)
        {
            new GitProcess(enlistment).InvokeGitOutsideEnlistment(
                "credential reject",
                stdin => stdin.Write("url=" + enlistment.RepoUrl + "\n\n"),
                null);
        }

        public static Result Version(Enlistment enlistment)
        {
            return new GitProcess(enlistment).InvokeGitOutsideEnlistment("--version");
        }
        
        public bool IsValidRepo()
        {
            Result result = this.InvokeGitAgainstDotGitFolder("rev-parse --show-toplevel");
            return !result.HasErrors;
        }

        public Result RevParse(string gitRef)
        {
            return this.InvokeGitAgainstDotGitFolder("rev-parse " + gitRef);
        }
        
        public string GetRepoRoot()
        {
            Result result = this.InvokeGitAgainstDotGitFolder("rev-parse --show-toplevel");
            if (result.HasErrors)
            {
                throw new InvalidRepoException(result.Errors);
            }

            return result.Output.TrimEnd('\r', '\n').Replace("/", "\\");
        }

        public void DeleteFromLocalConfig(string settingName)
        {
            this.InvokeGitAgainstDotGitFolder("config --local --unset-all " + settingName);
        }

        public Result SetInLocalConfig(string settingName, string value, bool replaceAll = false)
        {
            return this.InvokeGitAgainstDotGitFolder(string.Format(
                "config --local {0} {1} {2}",
                 replaceAll ? "--replace-all " : string.Empty,
                 settingName,
                 value));
        }

        public Result GetAllLocalConfig()
        {
            return this.InvokeGitAgainstDotGitFolder("config --list --local");
        }

        public string GetFromConfig(string settingName)
        {
            // This method is called at clone time, so the physical repo may not exist yet.
            Result result = Directory.Exists(this.enlistment.WorkingDirectoryRoot) ?
                    this.InvokeGitAgainstDotGitFolder("config " + settingName) :
                    this.InvokeGitOutsideEnlistment("config " + settingName);

            // Git returns non-zero for non-existent settings and errors.
            if (!result.HasErrors)
            {
                return result.Output.TrimEnd('\n');
            }
            else if (result.Errors.Any())
            {
                throw new InvalidRepoException("Error while reading '" + settingName + "' from config: " + result.Errors);
            }

            return null;
        }

        public Result GetOriginUrl()
        {
            Result result = this.InvokeGitAgainstDotGitFolder("remote -v");
            if (result.HasErrors)
            {
                return result;
            }

            string[] lines = result.Output.Split('\r', '\n');
            string originFetchLine = lines.Where(
                l => l.StartsWith("origin", StringComparison.OrdinalIgnoreCase)
                    && l.EndsWith("(fetch)")).FirstOrDefault();
            if (originFetchLine == null)
            {
                throw new InvalidRepoException("remote 'origin' is not configured for this repo");
            }

            string[] parts = originFetchLine.Split('\t', ' ');
            return new Result(parts[1], string.Empty, 0);
        }

        public void DiffTree(string sourceTreeish, string targetTreeish, Action<string> onResult)
        {
            this.InvokeGitAgainstDotGitFolder("diff-tree -r -t " + sourceTreeish + " " + targetTreeish, null, onResult);
        }

        public Result DiffWithNameOnlyAndFilterForAddedAndReanamedFiles(string commitish1, string commitish2)
        {
            return this.InvokeGitInWorkingDirectoryRoot("diff --name-only --diff-filter=AR " + commitish1 + " " + commitish2, useReadObjectHook: true);
        }

        public Result CreateBranchWithUpstream(string branchToCreate, string upstreamBranch)
        {
            return this.InvokeGitAgainstDotGitFolder("branch --set-upstream " + branchToCreate + " " + upstreamBranch);
        }

        public Result ForceCheckout(string target)
        {
            return this.InvokeGitInWorkingDirectoryRoot("checkout -f " + target, useReadObjectHook: false);
        }

        public Result UpdateIndexVersion4()
        {
            return this.InvokeGitAgainstDotGitFolder("update-index --index-version 4");
        }

        public Result UnpackObjects(Stream packFileStream)
        {
            return this.InvokeGitAgainstDotGitFolder(
                "unpack-objects",
                stdin =>
                {
                    packFileStream.CopyTo(stdin.BaseStream);
                    stdin.Write('\n');
                },
                null);
        }

        public Result IndexPack(string packfilePath)
        {
            return this.InvokeGitAgainstDotGitFolder("index-pack \"" + packfilePath + "\"");
        }

        public Result RemoteAdd(string remoteName, string url)
        {
            return this.InvokeGitAgainstDotGitFolder("remote add " + remoteName + " " + url);
        }

        public Result CatFilePretty(string objectId)
        {
            return this.InvokeGitAgainstDotGitFolder("cat-file -p " + objectId);
        }

        public Result CatFileGetType(string objectId)
        {
            return this.InvokeGitAgainstDotGitFolder("cat-file -t " + objectId);
        }

        public Result CatFileBatchCheckAll(Action<string> parseStdOutLine)
        {
            return this.InvokeGitAgainstDotGitFolder("cat-file --batch-check --batch-all-objects", null, parseStdOutLine);
        }

        public Result LsTree(string treeish, Action<string> parseStdOutLine, bool recursive, bool showAllTrees = false)
        {
            return this.InvokeGitAgainstDotGitFolder(
                "ls-tree " + (recursive ? "-r " : string.Empty) + (showAllTrees ? "-t " : string.Empty) + treeish, 
                null, 
                parseStdOutLine);
        }

        public Result SetUpstream(string branchName, string upstream)
        {
            return this.InvokeGitAgainstDotGitFolder("branch --set-upstream-to=" + upstream + " " + branchName);
        }

        public Result UpdateBranchSymbolicRef(string refToUpdate, string targetRef)
        {
            return this.InvokeGitAgainstDotGitFolder("symbolic-ref " + refToUpdate + " " + targetRef);
        }

        public Result UpdateBranchSha(string refToUpdate, string targetSha)
        {
            // If oldCommitResult doesn't fail, then the branch exists and update-ref will want the old sha
            Result oldCommitResult = this.RevParse(refToUpdate);
            string oldSha = string.Empty;
            if (!oldCommitResult.HasErrors)
            {
                oldSha = oldCommitResult.Output.TrimEnd('\n');
            }

            return this.InvokeGitAgainstDotGitFolder("update-ref --no-deref " + refToUpdate + " " + targetSha + " " + oldSha);
        }

        public Result ReadTree(string treeIsh)
        {
            return this.InvokeGitAgainstDotGitFolder("read-tree " + treeIsh);
        }
        
        public Process GetGitProcess(string command, string workingDirectory, string dotGitDirectory, bool useReadObjectHook, bool redirectStandardError)
        {
            ProcessStartInfo processInfo = new ProcessStartInfo(this.enlistment.GitBinPath);
            processInfo.WorkingDirectory = workingDirectory;
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardInput = true;
            processInfo.RedirectStandardOutput = true;
            processInfo.RedirectStandardError = redirectStandardError;
            processInfo.WindowStyle = ProcessWindowStyle.Hidden;

            processInfo.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
            processInfo.EnvironmentVariables["PATH"] =
                string.Join(
                    ";",
                    this.enlistment.GitBinPath,
                    this.enlistment.GVFSHooksRoot ?? string.Empty);

            if (!useReadObjectHook)
            { 
                command = "-c " + GVFSConstants.VirtualizeObjectsGitConfigName + "=false " + command;
            }

            if (!string.IsNullOrEmpty(dotGitDirectory))
            {
                command = "--git-dir=\"" + dotGitDirectory + "\" " + command;
            }

            processInfo.Arguments = command;

            Process executingProcess = new Process();
            executingProcess.StartInfo = processInfo;
            return executingProcess;
        }
        
        private static string ParseValue(string contents, string prefix)
        {
            int startIndex = contents.IndexOf(prefix) + prefix.Length;            
            if (startIndex >= 0 && startIndex < contents.Length)
            {
                int endIndex = contents.IndexOf('\n', startIndex);
                if (endIndex >= 0 && endIndex < contents.Length)
                {
                    return
                        contents
                        .Substring(startIndex, endIndex - startIndex)
                        .Trim('\r');
                }
            }

            return null;
        }
        
        /// <summary>
        /// Invokes git.exe without a working directory set.
        /// </summary>
        /// <remarks>
        /// For commands where git doesn't need to be (or can't be) run from inside an enlistment.
        /// eg. 'git init' or 'git credential'
        /// </remarks>
        private Result InvokeGitOutsideEnlistment(string command)
        {
            return this.InvokeGitOutsideEnlistment(command, null, null);
        }

        private Result InvokeGitOutsideEnlistment(
            string command,
            Action<StreamWriter> writeStdIn,
            Action<string> parseStdOutLine,
            int timeout = -1)
        {
            return this.InvokeGitImpl(
                command, 
                workingDirectory: Environment.SystemDirectory,
                dotGitDirectory: null,
                useReadObjectHook: false,
                writeStdIn: writeStdIn, 
                parseStdOutLine: parseStdOutLine, 
                timeoutMs: timeout);
        }

        /// <summary>
        /// Invokes git.exe from an enlistment's repository root
        /// </summary>
        private Result InvokeGitInWorkingDirectoryRoot(string command, bool useReadObjectHook)
        {
            return this.InvokeGitImpl(
                command,
                workingDirectory: this.enlistment.WorkingDirectoryRoot, 
                dotGitDirectory: null, 
                useReadObjectHook: useReadObjectHook, 
                writeStdIn: null, 
                parseStdOutLine: null, 
                timeoutMs: -1);
        }

        /// <summary>
        /// Invokes git.exe against an enlistment's .git folder.
        /// This method should be used only with git-commands that ignore the working directory
        /// </summary>
        private Result InvokeGitAgainstDotGitFolder(string command)
        {
            return this.InvokeGitAgainstDotGitFolder(command, null, null);
        }

        private Result InvokeGitAgainstDotGitFolder(
            string command,
            Action<StreamWriter> writeStdIn,
            Action<string> parseStdOutLine)
        {
            // This git command should not need/use the working directory of the repo.
            // Run git.exe in Environment.SystemDirectory to ensure the git.exe process
            // does not touch the working directory
            return this.InvokeGitImpl(
                command,
                workingDirectory: Environment.SystemDirectory,
                dotGitDirectory: this.enlistment.DotGitRoot,
                useReadObjectHook: false,
                writeStdIn: writeStdIn,
                parseStdOutLine: parseStdOutLine, 
                timeoutMs: -1);
        }

        private Result InvokeGitImpl(
            string command, 
            string workingDirectory,
            string dotGitDirectory,
            bool useReadObjectHook,
            Action<StreamWriter> writeStdIn, 
            Action<string> parseStdOutLine,
            int timeoutMs)
        {
            try
            {
                // From https://msdn.microsoft.com/en-us/library/system.diagnostics.process.standardoutput.aspx
                // To avoid deadlocks, use asynchronous read operations on at least one of the streams.
                // Do not perform a synchronous read to the end of both redirected streams.
                using (Process executingProcess = this.GetGitProcess(command, workingDirectory, dotGitDirectory, useReadObjectHook, redirectStandardError: true))
                {
                    StringBuilder output = new StringBuilder();
                    StringBuilder errors = new StringBuilder();

                    executingProcess.ErrorDataReceived += (sender, args) =>
                    {
                        if (args.Data != null)
                        {
                            errors.Append(args.Data + "\n");
                        }
                    };
                    executingProcess.OutputDataReceived += (sender, args) =>
                    {
                        if (args.Data != null)
                        {
                            if (parseStdOutLine != null)
                            {
                                parseStdOutLine(args.Data);
                            }
                            else
                            {
                                output.Append(args.Data + "\n");
                            }
                        }
                    };

                    lock (this.executionLock)
                    {
                        executingProcess.Start();

                        if (writeStdIn != null)
                        {
                            writeStdIn(executingProcess.StandardInput);
                        }

                        executingProcess.BeginOutputReadLine();
                        executingProcess.BeginErrorReadLine();

                        if (!executingProcess.WaitForExit(timeoutMs))
                        {
                            executingProcess.Kill();
                            return new Result(output.ToString(), "Operation timed out: " + errors.ToString(), Result.GenericFailureCode);
                        }
                    }

                    return new Result(output.ToString(), errors.ToString(), executingProcess.ExitCode);
                }
            }
            catch (Win32Exception e)
            {
                return new Result(string.Empty, e.Message, Result.GenericFailureCode);
            }
        }

        public class Result
        {
            public const int SuccessCode = 0;
            public const int GenericFailureCode = 1;

            public Result(string output, string errors, int returnCode)
            {
                this.Output = output;
                this.Errors = errors;
                this.ReturnCode = returnCode;
            }

            public string Output { get; }
            public string Errors { get; }
            public int ReturnCode { get; }

            public bool HasErrors
            {
                get { return this.ReturnCode != SuccessCode; }
            }
        }
    }
}
