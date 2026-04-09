using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using GVFS.DiskLayoutUpgrades;
using System;
using System.Collections.Generic;
using System.IO;

namespace GVFS.Platform.Windows.DiskLayoutUpgrades
{
    public class DiskLayout14to15Upgrade_ModifiedPaths : DiskLayoutUpgrade.MajorUpgrade
    {
        protected override int SourceMajorVersion => 14;

        public override bool TryUpgrade(ITracer tracer, string enlistmentRoot)
        {
            ModifiedPathsDatabase modifiedPaths = null;
            try
            {
                PhysicalFileSystem fileSystem = new PhysicalFileSystem();

                string modifiedPathsDatabasePath = Path.Combine(enlistmentRoot, GVFSPlatform.Instance.Constants.DotGVFSRoot, GVFSConstants.DotGVFS.Databases.ModifiedPaths);
                string error;
                if (!ModifiedPathsDatabase.TryLoadOrCreate(tracer, modifiedPathsDatabasePath, fileSystem, out modifiedPaths, out error))
                {
                    tracer.RelatedError($"Unable to create the modified paths database. {error}");
                    return false;
                }

                string sparseCheckoutPath = Path.Combine(enlistmentRoot, GVFSConstants.WorkingDirectoryRootName, GVFSConstants.DotGit.Info.SparseCheckoutPath);
                bool isRetryable;
                using (FileStream fs = File.OpenRead(sparseCheckoutPath))
                using (StreamReader reader = new StreamReader(fs))
                {
                    string entry = reader.ReadLine();
                    while (entry != null)
                    {
                        entry = entry.Trim();
                        if (!string.IsNullOrWhiteSpace(entry))
                        {
                            bool isFolder = entry.EndsWith(GVFSConstants.GitPathSeparatorString);
                            if (!modifiedPaths.TryAdd(entry.Trim(GVFSConstants.GitPathSeparator), isFolder, out isRetryable))
                            {
                                tracer.RelatedError("Unable to add to the modified paths database.");
                                return false;
                            }
                        }

                        entry = reader.ReadLine();
                    }
                }

                string alwaysExcludePath = Path.Combine(enlistmentRoot, GVFSConstants.WorkingDirectoryRootName, GVFSConstants.DotGit.Info.AlwaysExcludePath);
                if (fileSystem.FileExists(alwaysExcludePath))
                {
                    string alwaysExcludeData = fileSystem.ReadAllText(alwaysExcludePath);

                    char[] carriageReturnOrLineFeed = new[] { '\r', '\n' };
                    int endPosition = alwaysExcludeData.Length;
                    while (endPosition > 0)
                    {
                        int startPosition = alwaysExcludeData.LastIndexOfAny(carriageReturnOrLineFeed, endPosition - 1);
                        if (startPosition < 0)
                        {
                            startPosition = 0;
                        }

                        string entry = alwaysExcludeData.Substring(startPosition, endPosition - startPosition).Trim();

                        if (entry.EndsWith("*"))
                        {
                            // This is the first entry using the old format and we don't want to process old entries
                            // because we would need folder entries since there isn't a file and that would cause sparse-checkout to
                            // recursively clear skip-worktree bits for everything under that folder
                            break;
                        }

                        // Substring will not return a null and the Trim will get rid of all the whitespace
                        // if there is a length it will be a valid path that we need to process
                        if (entry.Length > 0)
                        {
                            entry = entry.TrimStart('!');
                            bool isFolder = entry.EndsWith(GVFSConstants.GitPathSeparatorString);
                            if (!isFolder)
                            {
                                if (!modifiedPaths.TryAdd(entry.Trim(GVFSConstants.GitPathSeparator), isFolder, out isRetryable))
                                {
                                    tracer.RelatedError("Unable to add to the modified paths database.");
                                    return false;
                                }
                            }
                        }

                        endPosition = startPosition;
                    }
                }

                modifiedPaths.ForceFlush();
                fileSystem.WriteAllText(sparseCheckoutPath, "/.gitattributes" + Environment.NewLine);
                fileSystem.DeleteFile(alwaysExcludePath);
            }
            catch (IOException ex)
            {
                tracer.RelatedError($"IOException: {ex.ToString()}");
                return false;
            }
            finally
            {
                if (modifiedPaths != null)
                {
                    modifiedPaths.Dispose();
                    modifiedPaths = null;
                }
            }

            if (!this.TryIncrementMajorVersion(tracer, enlistmentRoot))
            {
                return false;
            }

            return true;
        }
    }
}
