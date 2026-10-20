using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO;

namespace GVFS.Common
{
    public class RepoMetadata
    {
        private FileBasedDictionary<string, string> repoMetadata;
        private ITracer tracer;

        private RepoMetadata(ITracer tracer)
        {
            this.tracer = tracer;
        }

        public static RepoMetadata Instance { get; private set; }

        public string EnlistmentId
        {
            get
            {
                string value;
                if (!this.repoMetadata.TryGetValue(Keys.EnlistmentId, out value))
                {
                    value = CreateNewEnlistmentId(this.tracer);
                    this.repoMetadata.SetValueAndFlush(Keys.EnlistmentId, value);
                }

                return value;
            }
        }

        public string DataFilePath
        {
            get { return this.repoMetadata.DataFilePath; }
        }

        public static bool TryInitialize(ITracer tracer, string dotGVFSPath, out string error)
        {
            return TryInitialize(tracer, new PhysicalFileSystem(), dotGVFSPath, out error);
        }

        public static bool TryInitialize(ITracer tracer, PhysicalFileSystem fileSystem, string dotGVFSPath, out string error)
        {
            string dictionaryPath = Path.Combine(dotGVFSPath, GVFSConstants.DotGVFS.Databases.RepoMetadata);
            if (Instance != null)
            {
                if (!Instance.repoMetadata.DataFilePath.Equals(dictionaryPath, GVFSPlatform.Instance.Constants.PathComparison))
                {
                    throw new InvalidOperationException(
                        string.Format(
                            "TryInitialize should never be called twice with different parameters. Expected: '{0}' Actual: '{1}'",
                            Instance.repoMetadata.DataFilePath,
                            dictionaryPath));
                }
            }
            else
            {
                Instance = new RepoMetadata(tracer);
                if (!FileBasedDictionary<string, string>.TryCreate(
                    tracer,
                    dictionaryPath,
                    fileSystem,
                    out Instance.repoMetadata,
                    out error))
                {
                    return false;
                }
            }

            error = null;
            return true;
        }

        /// <summary>
        /// Reads the enlistment id and the mount id from the repo metadata in
        /// <paramref name="dotGVFSPath"/> without initializing <see cref="Instance"/>.
        /// A missing metadata file or a missing value produces an empty string.
        /// </summary>
        public static bool TryReadIds(string dotGVFSPath, out string enlistmentId, out string mountId, out string error)
        {
            enlistmentId = string.Empty;
            mountId = string.Empty;

            string dictionaryPath = Path.Combine(dotGVFSPath, GVFSConstants.DotGVFS.Databases.RepoMetadata);
            if (!File.Exists(dictionaryPath))
            {
                error = null;
                return true;
            }

            try
            {
                FileBasedDictionary<string, string> metadata;
                if (!FileBasedDictionary<string, string>.TryCreate(NullTracer.Instance, dictionaryPath, new PhysicalFileSystem(), out metadata, out error))
                {
                    return false;
                }

                using (metadata)
                {
                    string value;
                    if (metadata.TryGetValue(Keys.EnlistmentId, out value))
                    {
                        enlistmentId = value;
                    }

                    if (metadata.TryGetValue(Keys.MountId, out value))
                    {
                        mountId = value;
                    }
                }
            }
            catch (FileBasedCollectionException e)
            {
                error = e.Message;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Creates and persists an enlistment id in the repo metadata in
        /// <paramref name="dotGVFSPath"/> when it has none, without initializing
        /// <see cref="Instance"/>.
        /// </summary>
        public static bool TryEnsureEnlistmentId(ITracer tracer, string dotGVFSPath, out string error)
        {
            return TryEnsureEnlistmentId(tracer, new PhysicalFileSystem(), dotGVFSPath, out error);
        }

        public static bool TryEnsureEnlistmentId(ITracer tracer, PhysicalFileSystem fileSystem, string dotGVFSPath, out string error)
        {
            string dictionaryPath = Path.Combine(dotGVFSPath, GVFSConstants.DotGVFS.Databases.RepoMetadata);
            try
            {
                FileBasedDictionary<string, string> metadata;
                if (!FileBasedDictionary<string, string>.TryCreate(tracer, dictionaryPath, fileSystem, out metadata, out error))
                {
                    return false;
                }

                using (metadata)
                {
                    string existing;
                    if (!metadata.TryGetValue(Keys.EnlistmentId, out existing) || string.IsNullOrWhiteSpace(existing))
                    {
                        metadata.SetValueAndFlush(Keys.EnlistmentId, CreateNewEnlistmentId(tracer));
                    }
                }
            }
            catch (FileBasedCollectionException e)
            {
                error = e.Message;
                return false;
            }

            error = null;
            return true;
        }

        public static void Shutdown()
        {
            if (Instance != null)
            {
                if (Instance.repoMetadata != null)
                {
                    Instance.repoMetadata.Dispose();
                    Instance.repoMetadata = null;
                }

                Instance = null;
            }
        }

        public bool TryGetOnDiskLayoutVersion(out int majorVersion, out int minorVersion, out string error)
        {
            majorVersion = 0;
            minorVersion = 0;

            try
            {
                string value;
                if (!this.repoMetadata.TryGetValue(Keys.DiskLayoutMajorVersion, out value))
                {
                    error = "Enlistment disk layout version not found, check if a breaking change has been made to GVFS since cloning this enlistment.";
                    return false;
                }

                if (!int.TryParse(value, out majorVersion))
                {
                    error = "Failed to parse persisted disk layout version number: " + value;
                    return false;
                }

                // The minor version is optional, e.g. it could be missing during an upgrade
                if (this.repoMetadata.TryGetValue(Keys.DiskLayoutMinorVersion, out value))
                {
                    if (!int.TryParse(value, out minorVersion))
                    {
                        minorVersion = 0;
                    }
                }
            }
            catch (FileBasedCollectionException ex)
            {
                error = ex.Message;
                return false;
            }

            error = null;
            return true;
        }

        public void SaveCloneMetadata(ITracer tracer, GVFSEnlistment enlistment)
        {
            this.SaveMetadata(enlistment, CreateNewEnlistmentId(tracer));
        }

        /// <summary>
        /// Saves the metadata that a worktree mount copies from the primary
        /// enlistment. Unlike <see cref="SaveCloneMetadata"/>, it keeps an
        /// existing enlistment id, so the worktree id is stable across mounts.
        /// </summary>
        public void SaveWorktreeMetadata(ITracer tracer, GVFSEnlistment enlistment)
        {
            string enlistmentId;
            if (!this.repoMetadata.TryGetValue(Keys.EnlistmentId, out enlistmentId) || string.IsNullOrWhiteSpace(enlistmentId))
            {
                enlistmentId = CreateNewEnlistmentId(tracer);
            }

            this.SaveMetadata(enlistment, enlistmentId);
        }

        public void SetMountId(string mountId)
        {
            this.repoMetadata.SetValueAndFlush(Keys.MountId, mountId);
        }

        public void SetProjectionInvalid(bool invalid)
        {
            this.SetInvalid(Keys.ProjectionInvalid, invalid);
        }

        public bool GetProjectionInvalid()
        {
            return this.HasEntry(Keys.ProjectionInvalid);
        }

        public void SetPlaceholdersNeedUpdate(bool needUpdate)
        {
            this.SetInvalid(Keys.PlaceholdersNeedUpdate, needUpdate);
        }

        public bool GetPlaceholdersNeedUpdate()
        {
            return this.HasEntry(Keys.PlaceholdersNeedUpdate);
        }

        public void SetProjectionInvalidAndPlaceholdersNeedUpdate()
        {
            this.repoMetadata.SetValuesAndFlush(
                new[]
                {
                    new KeyValuePair<string, string>(Keys.ProjectionInvalid, bool.TrueString),
                    new KeyValuePair<string, string>(Keys.PlaceholdersNeedUpdate, bool.TrueString)
                });
        }

        public bool TryGetGitObjectsRoot(out string gitObjectsRoot, out string error)
        {
            gitObjectsRoot = null;

            try
            {
                if (!this.repoMetadata.TryGetValue(Keys.GitObjectsRoot, out gitObjectsRoot))
                {
                    error = "Git objects root not found";
                    return false;
                }
            }
            catch (FileBasedCollectionException ex)
            {
                error = ex.Message;
                return false;
            }

            error = null;
            return true;
        }

        public void SetGitObjectsRoot(string gitObjectsRoot)
        {
            this.repoMetadata.SetValueAndFlush(Keys.GitObjectsRoot, gitObjectsRoot);
        }

        public bool TryGetLocalCacheRoot(out string localCacheRoot, out string error)
        {
            localCacheRoot = null;

            try
            {
                if (!this.repoMetadata.TryGetValue(Keys.LocalCacheRoot, out localCacheRoot))
                {
                    error = "Local cache root not found";
                    return false;
                }
            }
            catch (FileBasedCollectionException ex)
            {
                error = ex.Message;
                return false;
            }

            error = null;
            return true;
        }

        public void SetLocalCacheRoot(string localCacheRoot)
        {
            this.repoMetadata.SetValueAndFlush(Keys.LocalCacheRoot, localCacheRoot);
        }

        public bool TryGetBlobSizesRoot(out string blobSizesRoot, out string error)
        {
            blobSizesRoot = null;

            try
            {
                if (!this.repoMetadata.TryGetValue(Keys.BlobSizesRoot, out blobSizesRoot))
                {
                    error = "Blob sizes root not found";
                    return false;
                }
            }
            catch (FileBasedCollectionException ex)
            {
                error = ex.Message;
                return false;
            }

            error = null;
            return true;
        }

        public void SetBlobSizesRoot(string blobSizesRoot)
        {
            this.repoMetadata.SetValueAndFlush(Keys.BlobSizesRoot, blobSizesRoot);
        }

        public void SetEntry(string keyName, string valueName)
        {
            this.repoMetadata.SetValueAndFlush(keyName, valueName);
        }

        private static string CreateNewEnlistmentId(ITracer tracer)
        {
            string enlistmentId = Guid.NewGuid().ToString("N");
            EventMetadata metadata = new EventMetadata();
            metadata.Add(nameof(enlistmentId), enlistmentId);
            tracer.RelatedEvent(EventLevel.Informational, nameof(CreateNewEnlistmentId), metadata);
            return enlistmentId;
        }

        private void SaveMetadata(GVFSEnlistment enlistment, string enlistmentId)
        {
            this.repoMetadata.SetValuesAndFlush(
                new[]
                {
                    new KeyValuePair<string, string>(Keys.DiskLayoutMajorVersion, GVFSPlatform.Instance.DiskLayoutUpgrade.Version.CurrentMajorVersion.ToString()),
                    new KeyValuePair<string, string>(Keys.DiskLayoutMinorVersion, GVFSPlatform.Instance.DiskLayoutUpgrade.Version.CurrentMinorVersion.ToString()),
                    new KeyValuePair<string, string>(Keys.GitObjectsRoot, enlistment.GitObjectsRoot),
                    new KeyValuePair<string, string>(Keys.LocalCacheRoot, enlistment.LocalCacheRoot),
                    new KeyValuePair<string, string>(Keys.BlobSizesRoot, enlistment.BlobSizesRoot),
                    new KeyValuePair<string, string>(Keys.EnlistmentId, enlistmentId),
                });
        }

        private void SetInvalid(string keyName, bool invalid)
        {
            if (invalid)
            {
                this.repoMetadata.SetValueAndFlush(keyName, bool.TrueString);
            }
            else
            {
                this.repoMetadata.RemoveAndFlush(keyName);
            }
        }

        private bool HasEntry(string keyName)
        {
            string value;
            if (this.repoMetadata.TryGetValue(keyName, out value))
            {
                return true;
            }

            return false;
        }

        public static class Keys
        {
            public const string ProjectionInvalid = "ProjectionInvalid";
            public const string PlaceholdersInvalid = "PlaceholdersInvalid";
            public const string DiskLayoutMajorVersion = "DiskLayoutVersion";
            public const string DiskLayoutMinorVersion = "DiskLayoutMinorVersion";
            public const string PlaceholdersNeedUpdate = "PlaceholdersNeedUpdate";
            public const string GitObjectsRoot = "GitObjectsRoot";
            public const string LocalCacheRoot = "LocalCacheRoot";
            public const string BlobSizesRoot = "BlobSizesRoot";
            public const string EnlistmentId = "EnlistmentId";
            public const string MountId = "MountId";
        }
    }
}
