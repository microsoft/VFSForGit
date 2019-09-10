using CommandLine;
using GVFS.Common;
using GVFS.Common.Database;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GVFS.CommandLine
{
    [Verb(
        SparseVerb.SparseVerbName,
        HelpText = @"EXPERIMENTAL: List, add, or remove from the list of folders that are included in VFS for Git's projection.
Folders need to be relative to the repos root directory.")
    ]
    public class SparseVerb : GVFSVerb.ForExistingEnlistment
    {
        private const string SparseVerbName = "sparse";
        private const string FolderListSeparator = ";";
        private const char StatusPathSeparatorToken = '\0';
        private const char StatusRenameToken = 'R';

        [Option(
            's',
            "set",
            Required = false,
            Default = "",
            HelpText = "A semicolon-delimited list of repo root relative folders to use as the sparse set for determining what to project. Wildcards are not supported.")]
        public string Set { get; set; }

        [Option(
            'f',
            "file",
            Required = false,
            Default = "",
            HelpText = "Path to a file that will has repo root relative folders to use as the sparse set. One folder per line. Wildcards are not supported.")]
        public string File { get; set; }

        [Option(
            'a',
            "add",
            Required = false,
            Default = "",
            HelpText = "A semicolon-delimited list of repo root relative folders to include in the sparse set for determining what to project. Wildcards are not supported.")]
        public string Add { get; set; }

        [Option(
            'r',
            "remove",
            Required = false,
            Default = "",
            HelpText = "A semicolon-delimited list of repo root relative folders to remove from the sparse set for determining what to project. Wildcards are not supported.")]
        public string Remove { get; set; }

        [Option(
            'l',
            "list",
            Required = false,
            Default = false,
            HelpText = "List of folders in the sparse set for determining what to project.")]
        public bool List { get; set; }

        [Option(
            'p',
            "prune",
            Required = false,
            Default = false,
            HelpText = "Remove any folders that are not in the list of sparse folders.")]
        public bool Prune { get; set; }

        protected override string VerbName => SparseVerbName;

        protected override void Execute(GVFSEnlistment enlistment)
        {
            if (this.List || (
                !this.Prune &&
                string.IsNullOrEmpty(this.Add) &&
                string.IsNullOrEmpty(this.Remove) &&
                string.IsNullOrEmpty(this.Set) &&
                string.IsNullOrEmpty(this.File)))
            {
                this.ListSparseFolders(enlistment.EnlistmentRoot);
                return;
            }

            if (!this.OptionsValid())
            {
                return;
            }

            using (JsonTracer tracer = new JsonTracer(GVFSConstants.GVFSEtwProviderName, SparseVerbName))
            {
                tracer.AddLogFileEventListener(
                    GVFSEnlistment.GetNewGVFSLogFileName(enlistment.GVFSLogsRoot, GVFSConstants.LogFileTypes.Sparse),
                    EventLevel.Informational,
                    Keywords.Any);

                HashSet<string> directories;
                bool needToChangeProjection = false;
                using (GVFSDatabase database = new GVFSDatabase(new PhysicalFileSystem(), enlistment.EnlistmentRoot, new SqliteDatabase()))
                {
                    SparseTable sparseTable = new SparseTable(database);
                    directories = sparseTable.GetAll();

                    List<string> foldersToRemove = new List<string>();
                    List<string> foldersToAdd = new List<string>();

                    if (!string.IsNullOrEmpty(this.Set) || !string.IsNullOrEmpty(this.File))
                    {
                        IEnumerable<string> folders = null;
                        if (!string.IsNullOrEmpty(this.Set))
                        {
                            folders = this.ParseFolderList(this.Set);
                        }
                        else if (!string.IsNullOrEmpty(this.File))
                        {
                            PhysicalFileSystem fileSystem = new PhysicalFileSystem();
                            folders = this.ParseFolderList(fileSystem.ReadAllText(this.File), folderSeparator: Environment.NewLine);
                        }
                        else
                        {
                            this.WriteMessage(tracer, "Invalid options specified.");
                            throw new InvalidOperationException();
                        }

                        foreach (string folder in folders)
                        {
                            if (!directories.Contains(folder))
                            {
                                needToChangeProjection = true;
                                foldersToAdd.Add(folder);
                            }
                            else
                            {
                                // Remove from directories so that the only directories left in the directories collection
                                // will be the ones that will need to be removed from sparse set
                                directories.Remove(folder);
                            }
                        }

                        if (directories.Count > 0)
                        {
                            needToChangeProjection = true;
                            foldersToRemove.AddRange(directories);
                        }
                    }
                    else
                    { // Process adds and removes
                        foreach (string folder in this.ParseFolderList(this.Remove))
                        {
                            if (directories.Contains(folder))
                            {
                                needToChangeProjection = true;
                                directories.Remove(folder);
                                foldersToRemove.Add(folder);
                            }
                        }

                        foreach (string folder in this.ParseFolderList(this.Add))
                        {
                            if (!directories.Contains(folder))
                            {
                                needToChangeProjection = true;
                                directories.Add(folder);
                                foldersToAdd.Add(folder);
                            }
                        }
                    }

                    if (needToChangeProjection || this.Prune)
                    {
                        if (directories.Count > 0)
                        {
                            // Make sure there is a clean git status before allowing sparse set to change
                            this.CheckGitStatus(tracer, enlistment, directories);
                        }

                        this.UpdateSparseFolders(tracer, sparseTable, foldersToRemove, foldersToAdd);
                    }

                    if (needToChangeProjection)
                    {
                        // Force a projection update to get the current inclusion set
                        this.ForceProjectionChange(tracer, enlistment);
                        tracer.RelatedInfo("Projection updated after adding or removing folders.");
                    }
                    else
                    {
                        this.WriteMessage(tracer, "No folders to update in sparse set.");
                    }

                    if (this.Prune && directories.Count > 0)
                    {
                        this.PruneFoldersOutsideSparse(tracer, enlistment, sparseTable);
                    }
                }
            }
        }

        private void PruneFoldersOutsideSparse(ITracer tracer, Enlistment enlistment, SparseTable sparseTable)
        {
            string[] directoriesToDehydrate = this.GetDirectoriesOutsideSparse(enlistment.WorkingDirectoryBackingRoot, sparseTable);
            if (directoriesToDehydrate.Length > 0)
            {
                if (!this.ShowStatusWhileRunning(
                    () =>
                    {
                        ReturnCode verbReturnCode = this.ExecuteGVFSVerb<DehydrateVerb>(
                            tracer,
                            verb =>
                            {
                                verb.Confirmed = true;
                                verb.Folders = string.Join(FolderListSeparator, directoriesToDehydrate);
                            });

                        return verbReturnCode == ReturnCode.Success;
                    },
                    "Pruning folders"))
                {
                    this.ReportErrorAndExit(tracer, "Failed to prune.");
                }
            }
        }

        private string[] GetDirectoriesOutsideSparse(string rootPath, SparseTable sparseTable)
        {
            HashSet<string> sparseFolders = sparseTable.GetAll();
            PhysicalFileSystem fileSystem = new PhysicalFileSystem();
            Queue<string> foldersToEnumerate = new Queue<string>();
            foldersToEnumerate.Enqueue(rootPath);

            List<string> foldersOutsideSparse = new List<string>();
            while (foldersToEnumerate.Count > 0)
            {
                string folderToEnumerate = foldersToEnumerate.Dequeue();
                foreach (string directory in fileSystem.EnumerateDirectories(folderToEnumerate))
                {
                    string enlistmentRootRelativeFolderPath = GVFSDatabase.NormalizePath(directory.Substring(rootPath.Length));
                    if (sparseFolders.Any(x => x.StartsWith(enlistmentRootRelativeFolderPath + Path.DirectorySeparatorChar, GVFSPlatform.Instance.Constants.PathComparison)))
                    {
                        foldersToEnumerate.Enqueue(directory);
                    }
                    else if (!sparseFolders.Contains(enlistmentRootRelativeFolderPath))
                    {
                        foldersOutsideSparse.Add(enlistmentRootRelativeFolderPath);
                    }
                }
            }

            return foldersOutsideSparse.ToArray();
        }

        private void UpdateSparseFolders(ITracer tracer, SparseTable sparseTable, List<string> foldersToRemove, List<string> foldersToAdd)
        {
            if (!this.ShowStatusWhileRunning(
                () =>
                {
                    foreach (string directoryPath in foldersToRemove)
                    {
                        tracer.RelatedInfo($"Removing '{directoryPath}' from sparse folders.");
                        sparseTable.Remove(directoryPath);
                    }

                    foreach (string directoryPath in foldersToAdd)
                    {
                        tracer.RelatedInfo($"Adding '{directoryPath}' to sparse folders.");
                        sparseTable.Add(directoryPath);
                    }

                    return true;
                },
                "Updating sparse folder set",
                suppressGvfsLogMessage: true))
            {
                this.ReportErrorAndExit(tracer, "Failed to update sparse folder set.");
            }
        }

        private bool OptionsValid()
        {
            if (!string.IsNullOrEmpty(this.Set) && (
                !string.IsNullOrEmpty(this.Add) ||
                !string.IsNullOrEmpty(this.Remove) ||
                !string.IsNullOrEmpty(this.File)))
            {
                this.Output.WriteLine("--set not valid with other options.");
                return false;
            }

            if (!string.IsNullOrEmpty(this.File) && (
                !string.IsNullOrEmpty(this.Add) ||
                !string.IsNullOrEmpty(this.Remove) ||
                !string.IsNullOrEmpty(this.Set)))
            {
                this.Output.WriteLine("--file not valid with other options.");
                return false;
            }

            return true;
        }

        private void ListSparseFolders(string enlistmentRoot)
        {
            using (GVFSDatabase database = new GVFSDatabase(new PhysicalFileSystem(), enlistmentRoot, new SqliteDatabase()))
            {
                SparseTable sparseTable = new SparseTable(database);
                HashSet<string> directories = sparseTable.GetAll();
                if (directories.Count == 0)
                {
                    this.Output.WriteLine("No folders in sparse list. When the sparse list is empty, all folders are projected.");
                }
                else
                {
                    foreach (string directory in directories)
                    {
                        this.Output.WriteLine(directory);
                    }
                }
            }
        }

        private IEnumerable<string> ParseFolderList(string folders, string folderSeparator = FolderListSeparator)
        {
            if (string.IsNullOrEmpty(folders))
            {
                return new string[0];
            }
            else
            {
                return folders.Split(new[] { folderSeparator }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => GVFSDatabase.NormalizePath(x));
            }
        }

        private void ForceProjectionChange(ITracer tracer, GVFSEnlistment enlistment)
        {
            string errorMessage = null;

            if (!this.ShowStatusWhileRunning(
                () =>
                {
                    NamedPipeMessages.PostIndexChanged.Response response = null;

                    try
                    {
                        using (NamedPipeClient pipeClient = new NamedPipeClient(enlistment.NamedPipeName))
                        {
                            if (!pipeClient.Connect())
                            {
                                this.ReportErrorAndExit("Unable to connect to GVFS.  Try running 'gvfs mount'");
                            }

                            NamedPipeMessages.PostIndexChanged.Request request = new NamedPipeMessages.PostIndexChanged.Request(updatedWorkingDirectory: true, updatedSkipWorktreeBits: false);
                            pipeClient.SendRequest(request.CreateMessage());
                            response = new NamedPipeMessages.PostIndexChanged.Response(NamedPipeMessages.Message.FromString(pipeClient.ReadRawResponse()).Header);
                            return response.Result == NamedPipeMessages.PostIndexChanged.SuccessResult;
                        }
                    }
                    catch (BrokenPipeException e)
                    {
                        this.ReportErrorAndExit("Unable to communicate with GVFS: " + e.ToString());
                        return false;
                    }
                },
                "Forcing a projection change",
                suppressGvfsLogMessage: true))
            {
                this.WriteMessage(tracer, "Failed to change projection: " + errorMessage);
            }
        }

        private void CheckGitStatus(ITracer tracer, GVFSEnlistment enlistment, HashSet<string> sparseFolders)
        {
            GitProcess.Result statusResult = null;
            if (!this.ShowStatusWhileRunning(
                () =>
                {
                    GitProcess git = new GitProcess(enlistment);
                    statusResult = git.StatusPorcelain();
                    if (statusResult.ExitCodeIsFailure)
                    {
                        return false;
                    }

                    if (this.ContainsPathNotCoveredBySparseFolders(statusResult.Output, sparseFolders))
                    {
                        return false;
                    }

                    return true;
                },
                "Running git status",
                suppressGvfsLogMessage: true))
            {
                this.Output.WriteLine();

                if (statusResult.ExitCodeIsFailure)
                {
                    this.WriteMessage(tracer, "Failed to run git status: " + statusResult.Errors);
                }
                else
                {
                    this.WriteMessage(tracer, statusResult.Output);
                    this.WriteMessage(tracer, "git status reported that you have dirty files");
                    this.WriteMessage(tracer, "Either commit your changes or reset and clean");
                }

                this.ReportErrorAndExit(tracer, SparseVerbName + " was aborted");
            }
        }

        private bool ContainsPathNotCoveredBySparseFolders(string statusOutput, HashSet<string> sparseFolders)
        {
            int index = 0;
            while (index < statusOutput.Length - 1)
            {
                bool isRename = statusOutput[index] == StatusRenameToken || statusOutput[index + 1] == StatusRenameToken;
                index = index + 3;
                if (!this.PathCoveredBySparseFolders(ref index, statusOutput, sparseFolders))
                {
                    return true;
                }

                if (isRename)
                {
                    if (!this.PathCoveredBySparseFolders(ref index, statusOutput, sparseFolders))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool PathCoveredBySparseFolders(ref int index, string statusOutput, HashSet<string> sparseFolders)
        {
            int endOfPathIndex = statusOutput.IndexOf(StatusPathSeparatorToken, index);
            string filePath = statusOutput.Substring(index, endOfPathIndex - index)
                .Replace(GVFSConstants.GitPathSeparator, Path.DirectorySeparatorChar);
            index = endOfPathIndex + 1;
            return sparseFolders.Any(x => filePath.StartsWith(x + Path.DirectorySeparatorChar, GVFSPlatform.Instance.Constants.PathComparison));
        }

        private void WriteMessage(ITracer tracer, string message)
        {
            this.Output.WriteLine(message);
            tracer.RelatedEvent(
                EventLevel.Informational,
                SparseVerbName,
                new EventMetadata
                {
                    { TracingConstants.MessageKey.InfoMessage, message }
                });
        }
    }
}
