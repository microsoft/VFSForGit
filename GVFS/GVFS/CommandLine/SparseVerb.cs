using CommandLine;
using GVFS.Common;
using GVFS.Common.Database;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
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
        private const string PruneOptionName = "prune";

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
            PruneOptionName,
            Required = false,
            Default = false,
            HelpText = "Remove any folders that are not in the list of sparse folders.")]
        public bool Prune { get; set; }

        [Option(
            'd',
            "disable",
            Required = false,
            Default = false,
            HelpText = "Disable the sparse feature.  This will remove all folders in the sparse list and start projecting all folders.")]
        public bool Disable { get; set; }

        protected override string VerbName => SparseVerbName;

        protected override void Execute(GVFSEnlistment enlistment)
        {
            if (this.List || (
                !this.Prune &&
                !this.Disable &&
                string.IsNullOrEmpty(this.Add) &&
                string.IsNullOrEmpty(this.Remove) &&
                string.IsNullOrEmpty(this.Set) &&
                string.IsNullOrEmpty(this.File)))
            {
                this.ListSparseFolders(enlistment.EnlistmentRoot);
                return;
            }

            this.CheckOptions();

            using (JsonTracer tracer = new JsonTracer(GVFSConstants.GVFSEtwProviderName, SparseVerbName))
            {
                tracer.AddLogFileEventListener(
                    GVFSEnlistment.GetNewGVFSLogFileName(enlistment.GVFSLogsRoot, GVFSConstants.LogFileTypes.Sparse),
                    EventLevel.Informational,
                    Keywords.Any);

                EventMetadata metadata = new EventMetadata();
                metadata.Add(nameof(this.Set), this.Set);
                metadata.Add(nameof(this.File), this.File);
                metadata.Add(nameof(this.Add), this.Add);
                metadata.Add(nameof(this.Remove), this.Remove);
                metadata.Add(nameof(this.Prune), this.Prune);
                metadata.Add(nameof(this.Disable), this.Disable);
                tracer.RelatedInfo(metadata, $"Running sparse");

                HashSet<string> directories;
                bool needToChangeProjection = false;
                using (GVFSDatabase database = new GVFSDatabase(new PhysicalFileSystem(), enlistment.EnlistmentRoot, new SqliteDatabase()))
                {
                    SparseTable sparseTable = new SparseTable(database);
                    directories = sparseTable.GetAll();

                    List<string> foldersToRemove = new List<string>();
                    List<string> foldersToAdd = new List<string>();

                    if (this.Disable)
                    {
                        if (directories.Count > 0)
                        {
                            this.WriteMessage(tracer, "Removing all folders from sparse list. When the sparse list is empty, all folders are projected.");
                            needToChangeProjection = true;
                            foldersToRemove.AddRange(directories);
                            directories.Clear();
                        }
                        else
                        {
                            return;
                        }
                    }
                    else if (!string.IsNullOrEmpty(this.Set) || !string.IsNullOrEmpty(this.File))
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
                            directories.Clear();
                        }

                        // Need to add folders that will be in the projection back into directories for the status check
                        foreach (string folder in folders)
                        {
                            directories.Add(folder);
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

                    if (needToChangeProjection || this.Prune)
                    {
                        // Update the last write times of the parents of folders being added/removed
                        // so that File Explorer will refresh them
                        UpdateParentFolderLastWriteTimes(tracer, enlistment.WorkingDirectoryBackingRoot, foldersToRemove, foldersToAdd);
                    }
                }
            }
        }

        private static void UpdateParentFolderLastWriteTimes(ITracer tracer, string rootPath, List<string> foldersToRemove, List<string> foldersToAdd)
        {
            Stopwatch updateTime = Stopwatch.StartNew();

            HashSet<string> foldersToRefresh = new HashSet<string>(GVFSPlatform.Instance.Constants.PathComparer);
            foreach (string path in foldersToRemove)
            {
                AddNonRootParentPathsToSet(foldersToRefresh, path);
            }

            foreach (string path in foldersToAdd)
            {
                AddNonRootParentPathsToSet(foldersToRefresh, path);
            }

            DateTime refreshTime = DateTime.Now;
            int foldersRefreshed = 0;
            int refreshErrors = 0;
            bool refreshFailed;

            // Always refresh the root
            if (SetFolderLastWriteTimeIfOnDisk(tracer, rootPath, refreshTime, out refreshFailed))
            {
                ++foldersRefreshed;
            }
            else if (refreshFailed)
            {
                ++refreshErrors;
            }

            string folderPathPrefix = $"{rootPath}{Path.DirectorySeparatorChar}";
            foreach (string path in foldersToRefresh)
            {
                if (SetFolderLastWriteTimeIfOnDisk(tracer, folderPathPrefix + path, refreshTime, out refreshFailed))
                {
                    ++foldersRefreshed;
                }
                else if (refreshFailed)
                {
                    ++refreshErrors;
                }
            }

            updateTime.Stop();

            // Note foldersToRefresh might be larger than foldersRefreshed + refreshErrors as
            // it could include folders that are not on disk
            EventMetadata metadata = new EventMetadata();
            metadata.Add("foldersToRefresh", foldersToRefresh.Count + 1); // +1 for the root
            metadata.Add(nameof(foldersRefreshed), foldersRefreshed);
            metadata.Add(nameof(refreshErrors), refreshErrors);
            metadata.Add(nameof(updateTime.ElapsedMilliseconds), updateTime.ElapsedMilliseconds);
            metadata.Add(TracingConstants.MessageKey.InfoMessage, "Updated folder last write times");
            tracer.RelatedEvent(EventLevel.Informational, $"{nameof(UpdateParentFolderLastWriteTimes)}_Summary", metadata);
        }

        private static void AddNonRootParentPathsToSet(HashSet<string> set, string path)
        {
            int lastSeparatorIndex = path.LastIndexOf(Path.DirectorySeparatorChar);
            string parentPath = path;
            while (lastSeparatorIndex > 0)
            {
                parentPath = parentPath.Substring(0, lastSeparatorIndex);
                set.Add(parentPath);
                lastSeparatorIndex = parentPath.LastIndexOf(Path.DirectorySeparatorChar);
            }
        }

        private static bool SetFolderLastWriteTimeIfOnDisk(ITracer tracer, string folderPath, DateTime time, out bool failed)
        {
            try
            {
                failed = false;
                return GVFSPlatform.Instance.FileSystem.SetDirectoryLastWriteTimeIfOnDisk(folderPath, time);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is Win32Exception)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Exception", e.ToString());
                metadata.Add(nameof(folderPath), folderPath);
                metadata.Add(TracingConstants.MessageKey.InfoMessage, $"{nameof(SetFolderLastWriteTimeIfOnDisk)}: Failed to update folder write time");
                tracer.RelatedEvent(EventLevel.Informational, $"{nameof(SetFolderLastWriteTimeIfOnDisk)}_FailedWriteTimeUpdate", metadata);

                failed = true;
                return false;
            }
        }

        private void PruneFoldersOutsideSparse(ITracer tracer, Enlistment enlistment, SparseTable sparseTable)
        {
            string[] directoriesToDehydrate = new string[0];
            if (!this.ShowStatusWhileRunning(
                () =>
                {
                    directoriesToDehydrate = this.GetDirectoriesOutsideSparse(enlistment.WorkingDirectoryBackingRoot, sparseTable);
                    return true;
                },
                $"Finding folders to {PruneOptionName}"))
            {
                this.ReportErrorAndExit(tracer, $"Failed to {PruneOptionName}.");
            }

            this.WriteMessage(tracer, $"Found {directoriesToDehydrate.Length} folders to {PruneOptionName}.");

            if (directoriesToDehydrate.Length > 0)
            {
                ReturnCode verbReturnCode = this.ExecuteGVFSVerb<DehydrateVerb>(
                    tracer,
                    verb =>
                    {
                        verb.RunningVerbName = this.VerbName;
                        verb.ActionName = PruneOptionName;
                        verb.Confirmed = true;
                        verb.Folders = string.Join(FolderListSeparator, directoriesToDehydrate);
                    },
                    this.Output);

                if (verbReturnCode != ReturnCode.Success)
                {
                    this.ReportErrorAndExit(tracer, verbReturnCode, $"Failed to {PruneOptionName}. Exit Code: {verbReturnCode}");
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
                    if (!enlistmentRootRelativeFolderPath.Equals(GVFSConstants.DotGit.Root, GVFSPlatform.Instance.Constants.PathComparison))
                    {
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

        private void CheckOptions()
        {
            if (this.Disable && (
                this.Prune ||
                !string.IsNullOrEmpty(this.Set) ||
                !string.IsNullOrEmpty(this.Add) ||
                !string.IsNullOrEmpty(this.Remove) ||
                !string.IsNullOrEmpty(this.File)))
            {
                this.ReportErrorAndExit("--disable not valid with other options.");
            }

            if (!string.IsNullOrEmpty(this.Set) && (
                !string.IsNullOrEmpty(this.Add) ||
                !string.IsNullOrEmpty(this.Remove) ||
                !string.IsNullOrEmpty(this.File)))
            {
                this.ReportErrorAndExit("--set not valid with other options.");
            }

            if (!string.IsNullOrEmpty(this.File) && (
                !string.IsNullOrEmpty(this.Add) ||
                !string.IsNullOrEmpty(this.Remove) ||
                !string.IsNullOrEmpty(this.Set)))
            {
                this.ReportErrorAndExit("--file not valid with other options.");
            }
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

                this.Output.WriteLine();
                this.ReportErrorAndExit(tracer, "Sparse was aborted.");
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
