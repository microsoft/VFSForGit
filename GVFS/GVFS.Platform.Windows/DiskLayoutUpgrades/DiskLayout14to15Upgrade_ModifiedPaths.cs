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

                string modifiedPathsDatabasePath = Path.Combine(enlistmentRoot, GVFSConstants.DotGVFS.Root, GVFSConstants.DotGVFS.Databases.ModifiedPaths);
                string error;
                if (!ModifiedPathsDatabase.TryLoadOrCreate(tracer, modifiedPathsDatabasePath, fileSystem, out modifiedPaths, out error))
                {
                    tracer.RelatedError($"Unable to create the modified paths database. {error}");
                    return false;
                }

                string sparseCheckoutPath = Path.Combine(enlistmentRoot, GVFSConstants.WorkingDirectoryRootName, GVFSConstants.DotGit.Info.SparseCheckoutPath);
                IEnumerable<string> sparseCheckoutLines = fileSystem.ReadAllText(sparseCheckoutPath).Split(new string[] { "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                bool isRetryable;
                foreach (string entry in sparseCheckoutLines)
                {
                    bool isFolder = entry.EndsWith(GVFSConstants.GitPathSeparatorString);
                    if (!modifiedPaths.TryAdd(entry.Trim(GVFSConstants.GitPathSeparator), isFolder, out isRetryable))
                    {
                        tracer.RelatedError("Unable to add to the modified paths database.");
                        return false;
                    }
                }

                string alwaysExcludePath = Path.Combine(enlistmentRoot, GVFSConstants.WorkingDirectoryRootName, GVFSConstants.DotGit.Info.AlwaysExcludePath);
                if (fileSystem.FileExists(alwaysExcludePath))
                {
                    string[] alwaysExcludeLines = fileSystem.ReadAllText(alwaysExcludePath).Split(new string[] { "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                    for (int i = alwaysExcludeLines.Length - 1; i >= 0; i--)
                    {
                        string entry = alwaysExcludeLines[i];
                        if (entry.EndsWith("*"))
                        {
                            // This is the first entry using the old format and we don't want to process old entries
                            // because we would need folder entries since there isn't a file and that would cause sparse-checkout to
                            // recursively clear skip-worktree bits for everything under that folder
                            break;
                        }

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
