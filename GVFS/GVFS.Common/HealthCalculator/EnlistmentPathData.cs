using GVFS.Common.Database;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.Common
{
    public class EnlistmentPathData
    {
        public List<string> GitFolderPaths;
        public List<string> GitFilePaths;
        public List<string> PlaceholderFolderPaths;
        public List<string> PlaceholderFilePaths;
        public List<string> ModifiedFolderPaths;
        public List<string> ModifiedFilePaths;
        public List<string> GitTrackingPaths;

        public EnlistmentPathData()
        {
            this.GitFolderPaths = new List<string>();
            this.GitFilePaths = new List<string>();
            this.PlaceholderFolderPaths = new List<string>();
            this.PlaceholderFilePaths = new List<string>();
            this.ModifiedFolderPaths = new List<string>();
            this.ModifiedFilePaths = new List<string>();
            this.GitTrackingPaths = new List<string>();
        }

        public void NormalizeAllPaths()
        {
            this.NormalizePaths(this.GitFolderPaths);
            this.NormalizePaths(this.GitFilePaths);
            this.NormalizePaths(this.PlaceholderFolderPaths);
            this.NormalizePaths(this.PlaceholderFilePaths);
            this.NormalizePaths(this.ModifiedFolderPaths);
            this.NormalizePaths(this.ModifiedFilePaths);
            this.NormalizePaths(this.GitTrackingPaths);

            this.ModifiedFilePaths = this.ModifiedFilePaths.Union(this.GitTrackingPaths).ToList();
        }

        /// <summary>
        /// Get two lists of placeholders, one containing the files and the other the directories
        /// Goes to the SQLite database for the placeholder lists
        /// </summary>
        /// <param name="enlistment">The current GVFS enlistment being operated on</param>
        public virtual void LoadPlaceholdersFromDatabase(GVFSEnlistment enlistment, HydrationSummaryDiagnostics diagnostics = null)
        {
            List<IPlaceholderData> filePlaceholders = new List<IPlaceholderData>();
            List<IPlaceholderData> folderPlaceholders = new List<IPlaceholderData>();

            using (GVFSDatabase database = new GVFSDatabase(new PhysicalFileSystem(), enlistment.DotGVFSRoot, new SqliteDatabase()))
            {
                PlaceholderTable placeholderTable = new PlaceholderTable(database);
                placeholderTable.GetAllEntries(out filePlaceholders, out folderPlaceholders);
            }

            this.PlaceholderFilePaths.AddRange(filePlaceholders.Select(placeholderData => placeholderData.Path));
            if (diagnostics != null)
            {
                diagnostics.PartialFolderCount = 0;
                diagnostics.ExpandedFolderCount = 0;
                diagnostics.PossibleTombstoneFolderCount = 0;
            }

            foreach (IPlaceholderData placeholder in folderPlaceholders)
            {
                this.PlaceholderFolderPaths.Add(placeholder.Path);
                if (diagnostics != null)
                {
                    if (placeholder.IsExpandedFolder)
                    {
                        diagnostics.ExpandedFolderCount++;
                    }
                    else if (placeholder.IsPossibleTombstoneFolder)
                    {
                        diagnostics.PossibleTombstoneFolderCount++;
                    }
                    else
                    {
                        diagnostics.PartialFolderCount++;
                    }
                }
            }
        }

        /// <summary>
        /// Call 'git ls-files' and 'git ls-tree' to get a list of all files and directories in the enlistment
        /// </summary>
        /// <param name="enlistment">The current GVFS enlistmetn being operated on</param>
        public void LoadPathsFromGitIndex(GVFSEnlistment enlistment)
        {
            List<string> skipWorktreeFiles = new List<string>();
            GitProcess gitProcess = new GitProcess(enlistment);

            GitProcess.Result fileResult = gitProcess.LsFiles(
                line =>
                {
                    if (line.First() == 'H')
                    {
                        skipWorktreeFiles.Add(TrimGitIndexLineWithSkipWorktree(line));
                    }

                    this.GitFilePaths.Add(TrimGitIndexLineWithSkipWorktree(line));
                });
            GitProcess.Result folderResult = gitProcess.LsTree(
                GVFSConstants.DotGit.HeadName,
                line =>
                {
                    this.GitFolderPaths.Add(TrimGitIndexLine(line));
                },
                recursive: true,
                showDirectories: true);

            this.GitTrackingPaths.AddRange(skipWorktreeFiles);
        }

        public virtual void LoadModifiedPaths(GVFSEnlistment enlistment, ITracer tracer, HydrationSummaryDiagnostics diagnostics = null)
        {
            if (TryLoadModifiedPathsFromPipe(enlistment, tracer, diagnostics))
            {
                if (diagnostics != null)
                {
                    diagnostics.ModifiedPathsState = HydrationDataState.Complete;
                }

                return;
            }

            // Most likely GVFS is not mounted. Give a basic effort to read the modified paths database.
            string filePath = Path.Combine(enlistment.DotGVFSRoot, GVFSConstants.DotGVFS.Databases.ModifiedPaths);
            try
            {
                using (FileStream file = File.Open(filePath, FileMode.OpenOrCreate, FileAccess.Read, FileShare.Read))
                using (StreamReader reader = new StreamReader(file))
                {
                    AddModifiedPaths(ReadModifiedPathDatabaseLines(reader, diagnostics));
                    if (diagnostics != null)
                    {
                        diagnostics.ModifiedPathsDiskOutcome = HydrationPathSourceOutcome.Success;
                        diagnostics.ModifiedPathsState = HydrationDataState.Complete;
                    }
                }
            }
            catch (Exception ex)
            {
                if (diagnostics != null)
                {
                    diagnostics.ModifiedPathsDiskOutcome = HydrationPathSourceOutcome.Failed;
                    diagnostics.ModifiedPathsState = this.ModifiedFilePaths.Count > 0 || this.ModifiedFolderPaths.Count > 0
                        ? HydrationDataState.Partial : HydrationDataState.Failed;
                    diagnostics.ModifiedPathsExceptionType = ex.GetType().FullName;
                    diagnostics.ModifiedPathsExceptionHResult = ex.HResult;
                }

                tracer.RelatedWarning($"Failed to read modified paths file at {filePath}: {ex}");
            }
        }

        internal IEnumerable<string> ReadModifiedPathDatabaseLines(StreamReader r, HydrationSummaryDiagnostics diagnostics = null)
        {
            while (!r.EndOfStream)
            {
                string line = r.ReadLine();
                if (line == null)
                {
                    continue;
                }

                const string LinePrefix = "A ";
                if (line.StartsWith(LinePrefix))
                {
                    if (diagnostics != null)
                    {
                        diagnostics.ModifiedPathsAddRecords++;
                    }

                    line = line.Substring(LinePrefix.Length);
                }
                else if (diagnostics != null)
                {
                    if (line.StartsWith("D "))
                    {
                        diagnostics.ModifiedPathsDeleteRecords++;
                    }
                    else
                    {
                        diagnostics.ModifiedPathsOtherRecords++;
                    }
                }

                yield return line;
            }
        }

        /// <summary>
        /// Talk to the mount process across the named pipe to get a list of the modified paths
        /// </summary>
        /// <remarks>If/when modified paths are moved to SQLite go there instead</remarks>
        /// <param name="enlistment">The enlistment being operated on</param>
        /// <returns>An array containing all of the modified paths in string format</returns>
        private bool TryLoadModifiedPathsFromPipe(GVFSEnlistment enlistment, ITracer tracer, HydrationSummaryDiagnostics diagnostics)
        {
            using (NamedPipeClient pipeClient = new NamedPipeClient(enlistment.NamedPipeName))
            {
                string[] modifiedPathsList = Array.Empty<string>();

                if (!pipeClient.Connect(timeoutMilliseconds: 3000, out Exception connectError))
                {
                    if (diagnostics != null)
                    {
                        diagnostics.ModifiedPathsPipeOutcome = connectError is TimeoutException
                            ? HydrationPathSourceOutcome.ConnectTimeout : HydrationPathSourceOutcome.ConnectFailed;
                        diagnostics.ModifiedPathsPipeExceptionType = connectError?.GetType().FullName;
                        diagnostics.ModifiedPathsPipeExceptionHResult = connectError?.HResult;
                    }

                    return false;
                }

                try
                {
                    NamedPipeMessages.Message modifiedPathsMessage = new NamedPipeMessages.Message(NamedPipeMessages.ModifiedPaths.ListRequest, NamedPipeMessages.ModifiedPaths.CurrentVersion);
                    pipeClient.SendRequest(modifiedPathsMessage);

                    NamedPipeMessages.Message modifiedPathsResponse = pipeClient.ReadResponse();
                    if (!modifiedPathsResponse.Header.Equals(NamedPipeMessages.ModifiedPaths.SuccessResult))
                    {
                        if (diagnostics != null)
                        {
                            diagnostics.ModifiedPathsPipeOutcome = modifiedPathsResponse.Header == NamedPipeMessages.MountNotReadyResult
                                ? HydrationPathSourceOutcome.MountNotReady
                                : modifiedPathsResponse.Header == NamedPipeMessages.ModifiedPaths.InvalidVersion
                                    ? HydrationPathSourceOutcome.InvalidVersion : HydrationPathSourceOutcome.UnexpectedResponse;
                        }

                        return false;
                    }

                    modifiedPathsList = modifiedPathsResponse.Body.Split(new char[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
                }
                catch (Exception ex)
                {
                    if (diagnostics != null)
                    {
                        diagnostics.ModifiedPathsPipeOutcome = HydrationPathSourceOutcome.Failed;
                        diagnostics.ModifiedPathsPipeExceptionType = ex.GetType().FullName;
                        diagnostics.ModifiedPathsPipeExceptionHResult = ex.HResult;
                    }

                    tracer.RelatedWarning($"Failed to load modified paths via named pipe: {ex}");
                    return false;
                }

                AddModifiedPaths(modifiedPathsList);
                if (diagnostics != null)
                {
                    diagnostics.ModifiedPathsPipeOutcome = HydrationPathSourceOutcome.Success;
                }

                return true;
            }
        }

        private void AddModifiedPaths(IEnumerable<string> modifiedPathsList)
        {
            foreach (string path in modifiedPathsList)
            {
                if (path.Last() == GVFSConstants.GitPathSeparator)
                {
                    path.TrimEnd(GVFSConstants.GitPathSeparator);
                    this.ModifiedFolderPaths.Add(path);
                }
                else
                {
                    this.ModifiedFilePaths.Add(path);
                }
            }
        }

        /// <summary>
        /// Parse a line of the git index coming from the ls-files endpoint in the git process to get the path to that files
        /// These paths begin with 'S' or 'H' depending on if they have the skip-worktree bit set
        /// </summary>
        /// <param name="line">The line from the output of the git index</param>
        /// <returns>The path extracted from the provided line of the git index</returns>
        private static string TrimGitIndexLineWithSkipWorktree(string line)
        {
            return line.Substring(line.IndexOf(' ') + 1);
        }

        private void NormalizePaths(List<string> paths)
        {
            for (int i = 0; i < paths.Count; i++)
            {
                paths[i] = paths[i].Replace(GVFSPlatform.GVFSPlatformConstants.PathSeparator, GVFSConstants.GitPathSeparator);
                paths[i] = paths[i].Trim(GVFSConstants.GitPathSeparator);
            }
        }

        /// <summary>
        /// Parse a line of the git index coming from the ls-tree endpoint in the git process to get the path to that file
        /// </summary>
        /// <param name="line">The line from the output of the git index</param>
        /// <returns>The path extracted from the provided line of the git index</returns>
        private static string TrimGitIndexLine(string line)
        {
            return line.Substring(line.IndexOf('\t') + 1);
        }
    }
}
