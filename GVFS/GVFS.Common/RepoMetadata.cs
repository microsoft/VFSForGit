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
        
        private RepoMetadata()
        {
        }

        public static RepoMetadata Instance { get; private set; }

        public string DataFilePath
        {
            get { return this.repoMetadata.DataFilePath; }
        }

        public static bool TryInitialize(ITracer tracer, string dotGVFSPath, out string error)
        {
            string dictionaryPath = Path.Combine(dotGVFSPath, GVFSConstants.DotGVFS.Databases.RepoMetadata);
            if (Instance != null)
            {
                if (!Instance.repoMetadata.DataFilePath.Equals(dictionaryPath, StringComparison.OrdinalIgnoreCase))
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
                Instance = new RepoMetadata();
                if (!FileBasedDictionary<string, string>.TryCreate(   
                    tracer,
                    dictionaryPath,
                    new PhysicalFileSystem(),
                    out Instance.repoMetadata,
                    out error))
                {
                    return false;
                }
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

        public int GetCurrentDiskLayoutVersion()
        {
            return DiskLayoutVersion.CurrentDiskLayoutVersion;
        }
        
        public bool TryGetOnDiskLayoutVersion(out int version, out string error)
        {
            version = -1;

            try
            {
                string value;
                if (!this.repoMetadata.TryGetValue(Keys.DiskLayoutVersion, out value))
                {
                    error = DiskLayoutVersion.MissingVersionError;
                    return false;
                }

                if (!int.TryParse(value, out version))
                {
                    error = "Failed to parse persisted disk layout version number: " + value;
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

        public void SaveCloneMetadata(string gitObjectsRoot, string localCacheRoot)
        {
            this.repoMetadata.SetValuesAndFlush(
                new[]
                {
                    new KeyValuePair<string, string>(Keys.DiskLayoutVersion, DiskLayoutVersion.CurrentDiskLayoutVersion.ToString()),
                    new KeyValuePair<string, string>(Keys.GitObjectsRoot, gitObjectsRoot),
                    new KeyValuePair<string, string>(Keys.LocalCacheRoot, localCacheRoot)
                });
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

        public void SetEntry(string keyName, string valueName)
        {
            this.repoMetadata.SetValueAndFlush(keyName, valueName);
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
            public const string DiskLayoutVersion = "DiskLayoutVersion";
            public const string PlaceholdersNeedUpdate = "PlaceholdersNeedUpdate";
            public const string GitObjectsRoot = "GitObjectsRoot";
            public const string LocalCacheRoot = "LocalCacheRoot";
        }

        public static class DiskLayoutVersion
        {
            // The current disk layout version.  This number should be bumped whenever a disk format change is made
            // that would impact and older GVFS's ability to mount the repo
            public const int CurrentDiskLayoutVersion = 12;

            public const string MissingVersionError = "Enlistment disk layout version not found, check if a breaking change has been made to GVFS since cloning this enlistment.";

            // MaxDiskLayoutVersion ensures that olders versions of GVFS will not try to mount newer enlistments (if the 
            // disk layout of the newer GVFS is incompatible).
            // GVFS will only mount if the disk layout version of the repo is <= MaxDiskLayoutVersion
            public const int MaxDiskLayoutVersion = CurrentDiskLayoutVersion;

            // MinDiskLayoutVersion ensures that GVFS will not attempt to mount an older repo if there has been a breaking format
            // change since that enlistment was cloned.
            //     - GVFS will only mount if the disk layout version of the repo is >= MinDiskLayoutVersion
            //     - Bump this version number only when a breaking change is being made (i.e. upgrade is not supported)
            public const int MinDiskLayoutVersion = 7;
        }
    }
}
