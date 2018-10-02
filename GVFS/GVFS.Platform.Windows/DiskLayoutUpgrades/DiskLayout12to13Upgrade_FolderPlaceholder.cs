using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using GVFS.DiskLayoutUpgrades;
using ProjFS;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.Platform.Windows.DiskLayoutUpgrades
{
    public class DiskLayout12to13Upgrade_FolderPlaceholder : DiskLayoutUpgrade.MajorUpgrade
    {
        protected override int SourceMajorVersion
        {
            get { return 12; }
        }

        /// <summary>
        /// Adds the folder placeholders to the placeholders list
        /// </summary>
        public override bool TryUpgrade(ITracer tracer, string enlistmentRoot)
        {
            string dotGVFSRoot = Path.Combine(enlistmentRoot, GVFSConstants.DotGVFS.Root);
            try
            {
                string error;
                PlaceholderListDatabase placeholders;
                if (!PlaceholderListDatabase.TryCreate(
                    tracer,
                    Path.Combine(dotGVFSRoot, GVFSConstants.DotGVFS.Databases.PlaceholderList),
                    new PhysicalFileSystem(),
                    out placeholders,
                    out error))
                {
                    tracer.RelatedError("Failed to open placeholder database: " + error);
                    return false;
                }

                using (placeholders)
                {
                    string workingDirectoryRoot = Path.Combine(enlistmentRoot, GVFSConstants.WorkingDirectoryRootName);

                    // Run through the folder placeholders adding to the placeholder list
                    IEnumerable<PlaceholderListDatabase.PlaceholderData> folderPlaceholderPaths = 
                        GetFolderPlaceholdersFromDisk(tracer, new PhysicalFileSystem(), workingDirectoryRoot)
                        .Select(x => x.Substring(workingDirectoryRoot.Length + 1))
                        .Select(x => new PlaceholderListDatabase.PlaceholderData(x, GVFSConstants.AllZeroSha));

                    List<PlaceholderListDatabase.PlaceholderData> placeholderEntries = placeholders.GetAllEntries();
                    placeholderEntries.AddRange(folderPlaceholderPaths);

                    placeholders.WriteAllEntriesAndFlush(placeholderEntries);
                }
            }
            catch (IOException ex)
            {
                tracer.RelatedError("Could not write to placeholder database: " + ex.ToString());
                return false;
            }
            catch (Exception ex)
            {
                tracer.RelatedError("Error updating placeholder database with folders: " + ex.ToString());
                return false;
            }

            if (!this.TryIncrementMajorVersion(tracer, enlistmentRoot))
            {
                return false;
            }

            return true;
        }

        private static IEnumerable<string> GetFolderPlaceholdersFromDisk(ITracer tracer, PhysicalFileSystem fileSystem, string path)
        {
            if (!fileSystem.IsSymLink(path))
            {
                foreach (string directory in fileSystem.EnumerateDirectories(path))
                {
                    if (!directory.EndsWith(Path.DirectorySeparatorChar + GVFSConstants.DotGit.Root))
                    {
                        OnDiskFileState fileState = OnDiskFileState.Full;
                        HResult result = Utils.GetOnDiskFileState(directory, out fileState);
                        if (result == HResult.Ok)
                        {
                            if (IsPlaceholder(fileState))
                            {
                                yield return directory;
                            }

                            // Recurse into placeholders and full folders skipping the tombstones
                            if (!IsTombstone(fileState))
                            {
                                foreach (string placeholderPath in GetFolderPlaceholdersFromDisk(tracer, fileSystem, directory))
                                {
                                    yield return placeholderPath;
                                }
                            }
                        }
                        else if (result != HResult.FileNotFound)
                        {
                            // FileNotFound is returned for tombstones when the filter is attached to the volume so we want to 
                            // just skip those folders.  Any other HResults may cause valid folder placeholders not to be written
                            // to the placeholder database so we want to error out on those.
                            throw new InvalidDataException($"Error getting on disk file state. HResult = {result} for {directory}");
                        }
                    }
                }
            }
        }

        private static bool IsTombstone(OnDiskFileState fileState)
        {
            return (fileState & OnDiskFileState.Tombstone) != 0;
        }

        private static bool IsPlaceholder(OnDiskFileState fileState)
        {
            return (fileState & (OnDiskFileState.DirtyPlaceholder | OnDiskFileState.HydratedPlaceholder | OnDiskFileState.Placeholder)) != 0;
        }
    }
}
