using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace GVFS.Common.Git
{
    public class GitProcess
    {
        private const int HResultEHANDLE = -2147024890; // 0x80070006 E_HANDLE

        private static readonly Encoding UTF8NoBOM = new UTF8Encoding(false);
        private static bool failedToSetEncoding = false;

        private object executionLock = new object();

        private string gitBinPath;
        private string workingDirectoryRoot;
        private string dotGitRoot;
        private string gvfsHooksRoot;

        static GitProcess()
        {
            // If the encoding is UTF8, .Net's default behavior will include a BOM
            // We need to use the BOM-less encoding because Git doesn't understand it
            if (Console.InputEncoding.CodePage == UTF8NoBOM.CodePage)
            {
                try
                {
                    Console.InputEncoding = UTF8NoBOM;
                }
                catch (IOException ex) when (ex.HResult == HResultEHANDLE)
                {
                    // If the standard input for a console is redirected / not available,
                    // then we might not be able to set the InputEncoding here.
                    // In practice, this can happen if we attempt to run a GitProcess from within a Service,
                    // such as GVFS.Service.
                    // Record that we failed to set the encoding, but do not quite the process.
                    // This means that git commands that use stdin will not work, but
                    // for our scenarios, we do not expect these calls at this this time.
                    // We will check and fail if we attempt to write to stdin in in a git call below.
                    GitProcess.failedToSetEncoding = true;
                }
            }
        }

        public GitProcess(Enlistment enlistment)
            : this(enlistment.GitBinPath, enlistment.WorkingDirectoryRoot, enlistment.GVFSHooksRoot)
        {
        }

        public GitProcess(string gitBinPath, string workingDirectoryRoot, string gvfsHooksRoot)
        {
            if (string.IsNullOrWhiteSpace(gitBinPath))
            {
                throw new ArgumentException(nameof(gitBinPath));
            }

            this.gitBinPath = gitBinPath;
            this.workingDirectoryRoot = workingDirectoryRoot;
            this.gvfsHooksRoot = gvfsHooksRoot;

            if (this.workingDirectoryRoot != null)
            {
                this.dotGitRoot = Path.Combine(this.workingDirectoryRoot, GVFSConstants.DotGit.Root);
            }
        }

        public static Result Init(Enlistment enlistment)
        {
            return new GitProcess(enlistment).InvokeGitOutsideEnlistment("init \"" + enlistment.WorkingDirectoryRoot + "\"");
        }

        public static Result GetFromGlobalConfig(string gitBinPath, string settingName)
        {
            return new GitProcess(gitBinPath, workingDirectoryRoot: null, gvfsHooksRoot: null).InvokeGitOutsideEnlistment("config --global " + settingName);
        }

        public static Result GetFromSystemConfig(string gitBinPath, string settingName)
        {
            return new GitProcess(gitBinPath, workingDirectoryRoot: null, gvfsHooksRoot: null).InvokeGitOutsideEnlistment("config --system " + settingName);
        }

        public static Result GetFromFileConfig(string gitBinPath, string configFile, string settingName)
        {
            return new GitProcess(gitBinPath, workingDirectoryRoot: null, gvfsHooksRoot: null).InvokeGitOutsideEnlistment("config --file " + configFile + " " + settingName);
        }

        public static bool TryGetVersion(string gitBinPath, out GitVersion gitVersion, out string error)
        {
            GitProcess gitProcess = new GitProcess(gitBinPath, null, null);
            Result result = gitProcess.InvokeGitOutsideEnlistment("--version");
            string version = result.Output;

            if (result.HasErrors || !GitVersion.TryParseGitVersionCommandResult(version, out gitVersion))
            {
                gitVersion = null;
                error = "Unable to determine installed git version. " + version;
                return false;
            }

            error = null;
            return true;
        }

        public virtual void RevokeCredential(string repoUrl)
        {
            this.InvokeGitOutsideEnlistment(
                "credential reject",
                stdin => stdin.Write("url=" + repoUrl + "\n\n"),
                null);
        }

        public virtual bool TryGetCredentials(
            ITracer tracer,
            string repoUrl,
            out string username,
            out string password)
        {
            username = null;
            password = null;

            using (ITracer activity = tracer.StartActivity("TryGetCredentials", EventLevel.Informational))
            {
                Result gitCredentialOutput = this.InvokeGitAgainstDotGitFolder(
                    "-c " + GitConfigSetting.CredentialUseHttpPath + "=true credential fill",
                    stdin => stdin.Write("url=" + repoUrl + "\n\n"),
                    parseStdOutLine: null);

                if (gitCredentialOutput.HasErrors)
                {
                    EventMetadata errorData = new EventMetadata();
                    tracer.RelatedWarning(
                        errorData,
                        "Git could not get credentials: " + gitCredentialOutput.Errors,
                        Keywords.Network | Keywords.Telemetry);

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

        public bool IsValidRepo()
        {
            Result result = this.InvokeGitAgainstDotGitFolder("rev-parse --show-toplevel");
            return !result.HasErrors;
        }

        public Result RevParse(string gitRef)
        {
            return this.InvokeGitAgainstDotGitFolder("rev-parse " + gitRef);
        }

        public Result GetCurrentBranchName()
        {
            return this.InvokeGitAgainstDotGitFolder("name-rev --name-only HEAD");
        }

        public void DeleteFromLocalConfig(string settingName)
        {
            this.InvokeGitAgainstDotGitFolder("config --local --unset-all " + settingName);
        }

        public Result SetInLocalConfig(string settingName, string value, bool replaceAll = false)
        {
            return this.InvokeGitAgainstDotGitFolder(string.Format(
                "config --local {0} \"{1}\" \"{2}\"",
                 replaceAll ? "--replace-all " : string.Empty,
                 settingName,
                 value));
        }

        public Result AddInLocalConfig(string settingName, string value)
        {
            return this.InvokeGitAgainstDotGitFolder(string.Format(
                "config --local --add {0} {1}",
                 settingName,
                 value));
        }

        public Result SetInFileConfig(string configFile, string settingName, string value, bool replaceAll = false)
        {
            return this.InvokeGitOutsideEnlistment(string.Format(
                "config --file {0} {1} \"{2}\" \"{3}\"",
                 configFile,
                 replaceAll ? "--replace-all " : string.Empty,
                 settingName,
                 value));
        }

        public bool TryGetAllConfig(bool localOnly, out Dictionary<string, GitConfigSetting> configSettings)
        {
            string localParameter = localOnly ? "--local" : string.Empty;
            Result result = this.InvokeGitAgainstDotGitFolder("config --list " + localParameter);
            if (result.HasErrors)
            {
                configSettings = null;
                return false;
            }

            configSettings = GitConfigHelper.ParseKeyValues(result.Output);
            return true;
        }

        /// <summary>
        /// Get the config value give a setting name
        /// </summary>
        /// <param name="settingName">The name of the config setting</param>
        /// <param name="forceOutsideEnlistment">
        /// If false, will run the call from inside the enlistment if the working dir found,
        /// otherwise it will run it from outside the enlistment.
        /// </param>
        /// <returns>The value found for the setting.</returns>
        public virtual Result GetFromConfig(string settingName, bool forceOutsideEnlistment = false, PhysicalFileSystem fileSystem = null)
        {
            string command = string.Format("config {0}", settingName);
            fileSystem = fileSystem ?? new PhysicalFileSystem();

            // This method is called at clone time, so the physical repo may not exist yet.
            return
                fileSystem.DirectoryExists(this.workingDirectoryRoot) && !forceOutsideEnlistment
                    ? this.InvokeGitAgainstDotGitFolder(command)
                    : this.InvokeGitOutsideEnlistment(command);
        }

        public Result GetFromLocalConfig(string settingName)
        {
            return this.InvokeGitAgainstDotGitFolder("config --local " + settingName);
        }

        /// <summary>
        /// Safely gets the config value give a setting name
        /// </summary>
        /// <param name="settingName">The name of the config setting</param>
        /// <param name="forceOutsideEnlistment">
        /// If false, will run the call from inside the enlistment if the working dir found,
        /// otherwise it will run it from outside the enlistment.
        /// </param>
        /// <param value>The value found for the config setting.</param>
        /// <returns>True if the config call was successful, false otherwise.</returns>
        public bool TryGetFromConfig(string settingName, bool forceOutsideEnlistment, out string value, PhysicalFileSystem fileSystem = null)
        {
            value = null;
            try
            {
                Result result = this.GetFromConfig(settingName, forceOutsideEnlistment, fileSystem);
                if (!result.HasErrors)
                {
                    value = result.Output;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        public Result GetOriginUrl()
        {
            return this.InvokeGitAgainstDotGitFolder("config --local remote.origin.url");
        }

        public Result DiffTree(string sourceTreeish, string targetTreeish, Action<string> onResult)
        {
            return this.InvokeGitAgainstDotGitFolder("diff-tree -r -t " + sourceTreeish + " " + targetTreeish, null, onResult);
        }

        public Result CreateBranchWithUpstream(string branchToCreate, string upstreamBranch)
        {
            return this.InvokeGitAgainstDotGitFolder("branch " + branchToCreate + " --track " + upstreamBranch);
        }

        public Result ForceCheckout(string target)
        {
            return this.InvokeGitInWorkingDirectoryRoot("checkout -f " + target, useReadObjectHook: false);
        }

        public Result Status(bool allowObjectDownloads, bool useStatusCache)
        {
            string command = useStatusCache ? "status" : "status --no-deserialize";
            return this.InvokeGitInWorkingDirectoryRoot(command, useReadObjectHook: allowObjectDownloads);
        }

        public Result SerializeStatus(bool allowObjectDownloads, string serializePath)
        {
            // specify ignored=matching and --untracked-files=complete
            // so the status cache can answer status commands run by Visual Studio
            // or tools with similar requirements.
            return this.InvokeGitInWorkingDirectoryRoot(
                string.Format("--no-optional-locks status \"--serialize={0}\" --ignored=matching --untracked-files=complete", serializePath),
                useReadObjectHook: allowObjectDownloads);
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

        /// <summary>
        /// Write a new commit graph in the specified pack directory. Crawl the given pack-
        /// indexes for commits and then close under everything reachable or exists in the
        /// previous graph file.
        ///
        /// This will update the graph-head file to point to the new commit graph and delete
        /// any expired graph files that previously existed.
        /// </summary>
        public Result WriteCommitGraph(string objectDir, List<string> packs)
        {
            string command = "commit-graph write --stdin-packs --append --object-dir \"" + objectDir + "\"";
            return this.InvokeGitInWorkingDirectoryRoot(
                command,
                useReadObjectHook: true,
                writeStdIn: writer =>
                {
                    foreach (string packIndex in packs)
                    {
                        writer.WriteLine(packIndex);
                    }

                    // We need to close stdin or else the process will not terminate.
                    writer.Close();
                });
        }

        public Result IndexPack(string packfilePath, string idxOutputPath)
        {
            return this.InvokeGitAgainstDotGitFolder($"index-pack -o \"{idxOutputPath}\" \"{packfilePath}\"");
        }

        /// <summary>
        /// Write a new multi-pack-index (MIDX) in the specified pack directory.
        /// 
        /// This will update the midx-head file to point to the new MIDX file.
        /// 
        /// If no new packfiles are found, then this is a no-op.
        /// </summary>
        public Result WriteMultiPackIndex(string packDir)
        {
            // We override the config settings so we keep writing the MIDX file even if it is disabled for reads.
            return this.InvokeGitAgainstDotGitFolder("-c core.midx=true midx --write --update-head --pack-dir \"" + packDir + "\"");
        }

        public Result RemoteAdd(string remoteName, string url)
        {
            return this.InvokeGitAgainstDotGitFolder("remote add " + remoteName + " " + url);
        }

        public Result CatFileGetType(string objectId)
        {
            return this.InvokeGitAgainstDotGitFolder("cat-file -t " + objectId);
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
            ProcessStartInfo processInfo = new ProcessStartInfo(this.gitBinPath);
            processInfo.WorkingDirectory = workingDirectory;
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardInput = true;
            processInfo.RedirectStandardOutput = true;
            processInfo.RedirectStandardError = redirectStandardError;
            processInfo.WindowStyle = ProcessWindowStyle.Hidden;
            processInfo.CreateNoWindow = true;

            processInfo.StandardOutputEncoding = UTF8NoBOM;
            processInfo.StandardErrorEncoding = UTF8NoBOM;

            // Removing trace variables that might change git output and break parsing
            // List of environment variables: https://git-scm.com/book/gr/v2/Git-Internals-Environment-Variables
            foreach (string key in processInfo.EnvironmentVariables.Keys.Cast<string>().ToList())
            {
                // If GIT_TRACE is set to a fully-rooted path, then Git sends the trace
                // output to that path instead of stdout (GIT_TRACE=1) or stderr (GIT_TRACE=2).
                if (key.StartsWith("GIT_TRACE", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        if (!Path.IsPathRooted(processInfo.EnvironmentVariables[key]))
                        {
                            processInfo.EnvironmentVariables.Remove(key);
                        }
                    }
                    catch (ArgumentException)
                    {
                        processInfo.EnvironmentVariables.Remove(key);
                    }
                }
            }

            processInfo.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
            processInfo.EnvironmentVariables["GCM_VALIDATE"] = "0";
            processInfo.EnvironmentVariables["PATH"] =
                string.Join(
                    ";",
                    this.gitBinPath,
                    this.gvfsHooksRoot ?? string.Empty);

            if (!useReadObjectHook)
            {
                command = "-c " + GitConfigSetting.CoreVirtualizeObjectsName + "=false " + command;
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

        protected virtual Result InvokeGitImpl(
            string command,
            string workingDirectory,
            string dotGitDirectory,
            bool useReadObjectHook,
            Action<StreamWriter> writeStdIn,
            Action<string> parseStdOutLine,
            int timeoutMs)
        {
            if (failedToSetEncoding && writeStdIn != null)
            {
                return new Result(string.Empty, "Attempting to use to stdin, but the process does not have the right input encodings set.", Result.GenericFailureCode);
            }

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
        /// eg. 'git init' or 'git version'
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
        private Result InvokeGitInWorkingDirectoryRoot(
            string command,
            bool useReadObjectHook,
            Action<StreamWriter> writeStdIn = null)
        {
            return this.InvokeGitImpl(
                command,
                workingDirectory: this.workingDirectoryRoot,
                dotGitDirectory: null,
                useReadObjectHook: useReadObjectHook,
                writeStdIn: writeStdIn,
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
                dotGitDirectory: this.dotGitRoot,
                useReadObjectHook: false,
                writeStdIn: writeStdIn,
                parseStdOutLine: parseStdOutLine,
                timeoutMs: -1);
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
