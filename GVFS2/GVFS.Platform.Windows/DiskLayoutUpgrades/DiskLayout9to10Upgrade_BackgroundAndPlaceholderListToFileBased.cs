using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using GVFS.DiskLayoutUpgrades;
using GVFS.GVFlt;
using GVFS.Virtualization.Background;
using Microsoft.Isam.Esent;
using Microsoft.Isam.Esent.Collections.Generic;
using System.Collections.Generic;
using System.IO;

namespace GVFS.Platform.Windows.DiskLayoutUpgrades
{
    public class DiskLayout9to10Upgrade_BackgroundAndPlaceholderListToFileBased : DiskLayoutUpgrade.MajorUpgrade
    {
        private const string EsentBackgroundOpsFolder = "BackgroundGitUpdates";
        private const string EsentPlaceholderListFolder = "PlaceholderList";

        protected override int SourceMajorVersion
        {
            get { return 9; }
        }

        /// <summary>
        /// Rewrites ESENT BackgroundGitUpdates and PlaceholderList DBs to flat formats
        /// </summary>
        public override bool TryUpgrade(ITracer tracer, string enlistmentRoot)
        {
            string dotGVFSRoot = Path.Combine(enlistmentRoot, GVFSPlatform.Instance.Constants.DotGVFSRoot);
            if (!this.UpdateBackgroundOperations(tracer, dotGVFSRoot))
            {
                return false;
            }

            if (!this.UpdatePlaceholderList(tracer, dotGVFSRoot))
            {
                return false;
            }

            if (!this.TryIncrementMajorVersion(tracer, enlistmentRoot))
            {
                return false;
            }

            return true;
        }

        private bool UpdatePlaceholderList(ITracer tracer, string dotGVFSRoot)
        {
            string esentPlaceholderFolder = Path.Combine(dotGVFSRoot, EsentPlaceholderListFolder);
            if (Directory.Exists(esentPlaceholderFolder))
            {
                string newPlaceholderFolder = Path.Combine(dotGVFSRoot, GVFSConstants.DotGVFS.Databases.PlaceholderList);
                try
                {
                    using (PersistentDictionary<string, string> oldPlaceholders =
                        new PersistentDictionary<string, string>(esentPlaceholderFolder))
                    {
                        string error;
                        LegacyPlaceholderListDatabase newPlaceholders;
                        if (!LegacyPlaceholderListDatabase.TryCreate(
                            tracer,
                            newPlaceholderFolder,
                            new PhysicalFileSystem(),
                            out newPlaceholders,
                            out error))
                        {
                            tracer.RelatedError("Failed to create new placeholder database: " + error);
                            return false;
                        }

                        using (newPlaceholders)
                        {
                            List<LegacyPlaceholderListDatabase.PlaceholderData> data = new List<LegacyPlaceholderListDatabase.PlaceholderData>();
                            foreach (KeyValuePair<string, string> kvp in oldPlaceholders)
                            {
                                tracer.RelatedInfo("Copying ESENT entry: {0} = {1}", kvp.Key, kvp.Value);
                                data.Add(new LegacyPlaceholderListDatabase.PlaceholderData(path: kvp.Key, fileShaOrFolderValue: kvp.Value));
                            }

                            newPlaceholders.WriteAllEntriesAndFlush(data);
                        }
                    }
                }
                catch (IOException ex)
                {
                    tracer.RelatedError("Could not write to new placeholder database: " + ex.Message);
                    return false;
                }
                catch (EsentException ex)
                {
                    tracer.RelatedError("Placeholder database appears to be from an older version of GVFS and corrupted: " + ex.Message);
                    return false;
                }

                string backupName;
                if (this.TryRenameFolderForDelete(tracer, esentPlaceholderFolder, out backupName))
                {
                    // If this fails, we leave behind cruft, but there's no harm because we renamed.
                    this.TryDeleteFolder(tracer, backupName);
                    return true;
                }
                else
                {
                    // To avoid double upgrading, we should rollback if we can't rename the old data
                    this.TryDeleteFile(tracer, RepoMetadata.Instance.DataFilePath);
                    return false;
                }
            }

            return true;
        }

        private bool UpdateBackgroundOperations(ITracer tracer, string dotGVFSRoot)
        {
            string esentBackgroundOpsFolder = Path.Combine(dotGVFSRoot, EsentBackgroundOpsFolder);
            if (Directory.Exists(esentBackgroundOpsFolder))
            {
                string newBackgroundOpsFolder = Path.Combine(dotGVFSRoot, GVFSConstants.DotGVFS.Databases.BackgroundFileSystemTasks);
                try
                {
                    using (PersistentDictionary<long, GVFltCallbacks.BackgroundGitUpdate> oldBackgroundOps =
                        new PersistentDictionary<long, GVFltCallbacks.BackgroundGitUpdate>(esentBackgroundOpsFolder))
                    {
                        string error;
                        FileSystemTaskQueue newBackgroundOps;
                        if (!FileSystemTaskQueue.TryCreate(
                            tracer,
                            newBackgroundOpsFolder,
                            new PhysicalFileSystem(),
                            out newBackgroundOps,
                            out error))
                        {
                            tracer.RelatedError("Failed to create new background operations folder: " + error);
                            return false;
                        }

                        using (newBackgroundOps)
                        {
                            foreach (KeyValuePair<long, GVFltCallbacks.BackgroundGitUpdate> kvp in oldBackgroundOps)
                            {
                                tracer.RelatedInfo("Copying ESENT entry: {0} = {1}", kvp.Key, kvp.Value);
                                newBackgroundOps.EnqueueAndFlush(
                                    new FileSystemTask(
                                        (FileSystemTask.OperationType)kvp.Value.Operation,
                                        kvp.Value.VirtualPath,
                                        kvp.Value.OldVirtualPath));
                            }
                        }
                    }
                }
                catch (IOException ex)
                {
                    tracer.RelatedError("Could not write to new background operations: " + ex.Message);
                    return false;
                }
                catch (EsentException ex)
                {
                    tracer.RelatedError("BackgroundOperations appears to be from an older version of GVFS and corrupted: " + ex.Message);
                    return false;
                }

                string backupName;
                if (this.TryRenameFolderForDelete(tracer, esentBackgroundOpsFolder, out backupName))
                {
                    // If this fails, we leave behind cruft, but there's no harm because we renamed.
                    this.TryDeleteFolder(tracer, backupName);
                    return true;
                }
                else
                {
                    // To avoid double upgrading, we should rollback if we can't rename the old data
                    this.TryDeleteFile(tracer, RepoMetadata.Instance.DataFilePath);
                    return false;
                }
            }

            return true;
        }
    }
}
