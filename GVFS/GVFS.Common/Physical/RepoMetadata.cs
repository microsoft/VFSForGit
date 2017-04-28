using Microsoft.Isam.Esent.Collections.Generic;
using System;
using System.IO;

namespace GVFS.Common.Physical
{
    public class RepoMetadata : IDisposable
    {
        private const string ProjectionInvalidKey = "ProjectionInvalid";
        private const string AlwaysExcludeInvalidKey = "AlwaysExcludeInvalid";

        private PersistentDictionary<string, string> repoMetadata;

        public RepoMetadata(string dotGVFSPath)
        {
            this.repoMetadata = new PersistentDictionary<string, string>(
                Path.Combine(dotGVFSPath, GVFSConstants.DatabaseNames.RepoMetadata));
        }

        public static int GetCurrentDiskLayoutVersion()
        {
            return DiskLayoutVersion.CurrentDiskLayoutVersion;
        }

        public static bool CheckDiskLayoutVersion(string dotGVFSPath, bool allowUpgrade, out string error)
        {
            if (!Directory.Exists(Path.Combine(dotGVFSPath, GVFSConstants.DatabaseNames.RepoMetadata)))
            {
                error = DiskLayoutVersion.MissingVersionError;
                return false;
            }

            try
            {
                using (RepoMetadata repoMetadata = new RepoMetadata(dotGVFSPath))
                {
                    return repoMetadata.CheckDiskLayoutVersion(allowUpgrade, out error);
                }
            }
            catch (Exception e)
            {
                error = "Failed to check disk layout version of enlistment, Exception: " + e.ToString();
                return false;
            }
        }

        public bool TryGetOnDiskLayoutVersion(out int version, out string error)
        {
            return DiskLayoutVersion.TryGetOnDiskLayoutVersion(this.repoMetadata, out version, out error);
        }

        /* TODO: Story 957530 Remove code using GVFS_HEAD with next breaking change. */
        public bool OnDiskVersionSupportsIndexProjection()
        {
            return DiskLayoutVersion.OnDiskVersionSupportsIndexProjection(this.repoMetadata);
        }

        public bool OnDiskVersionUsesAlwaysExclude()
        {
            return DiskLayoutVersion.OnDiskVersionUsesAlwaysExclude(this.repoMetadata);
        }

        public void SaveCurrentDiskLayoutVersion()
        {
            DiskLayoutVersion.SaveCurrentDiskLayoutVersion(this.repoMetadata);
        }

        public void SetProjectionInvalid(bool invalid)
        {
            if (invalid)
            {
                this.repoMetadata[ProjectionInvalidKey] = "true";
            }
            else
            {
                this.repoMetadata.Remove(ProjectionInvalidKey);
            }

            this.repoMetadata.Flush();
        }

        public bool GetProjectionInvalid()
        {
            string value;
            if (this.repoMetadata.TryGetValue(ProjectionInvalidKey, out value))
            {
                return true;
            }

            return false;
        }

        public void SetAlwaysExcludeInvalid(bool invalid)
        {
            if (invalid)
            {
                this.repoMetadata[AlwaysExcludeInvalidKey] = "true";
            }
            else
            {
                this.repoMetadata.Remove(AlwaysExcludeInvalidKey);
            }

            this.repoMetadata.Flush();
        }

        public bool GetAlwaysExcludeInvalid()
        {
            string value;
            if (this.repoMetadata.TryGetValue(AlwaysExcludeInvalidKey, out value))
            {
                return true;
            }

            return false;
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected void Dispose(bool disposing)
        {
            if (this.repoMetadata != null)
            {
                this.repoMetadata.Dispose();
                this.repoMetadata = null;
            }
        }

        private bool CheckDiskLayoutVersion(bool allowUpgrade, out string error)
        {
            return DiskLayoutVersion.CheckDiskLayoutVersion(this.repoMetadata, allowUpgrade, out error);
        }

        private static class DiskLayoutVersion
        {
            // The current disk layout version.  This number should be bumped whenever a disk format change is made
            // that would impact and older GVFS's ability to mount the repo
            public const int CurrentDiskLayoutVersion = 5;

            public const string MissingVersionError = "Enlistment disk layout version not found, check if a breaking change has been made to GVFS since cloning this enlistment.";
            private const string DiskLayoutVersionKey = "DiskLayoutVersion";

            // MaxDiskLayoutVersion ensures that olders versions of GVFS will not try to mount newer enlistments (if the 
            // disk layout of the newer GVFS is incompatible).
            // GVFS will only mount if the disk layout version of the repo is <= MaxDiskLayoutVersion
            private const int MaxDiskLayoutVersion = CurrentDiskLayoutVersion;

            // MinDiskLayoutVersion ensures that GVFS will not attempt to mount an older repo if there has been a breaking format
            // change since that enlistment was cloned.
            //     - GVFS will only mount if the disk layout version of the repo is >= MinDiskLayoutVersion
            //     - Bump this version number only when a breaking change is being made (i.e. upgrade is not supported)
            private const int MinDiskLayoutVersion = 3;

            // The first version that supported projection files via the git index
            private const int ProjectGitIndexVersion = 4;

            // The first version that used always_exclude instead of exclude
            private const int AlwaysExcludeVersion = 5;

            public static void SaveCurrentDiskLayoutVersion(PersistentDictionary<string, string> repoMetadata)
            {
                repoMetadata[DiskLayoutVersionKey] = CurrentDiskLayoutVersion.ToString();
                repoMetadata.Flush();
            }

            public static bool TryGetOnDiskLayoutVersion(PersistentDictionary<string, string> repoMetadata, out int version, out string error)
            {
                version = -1;
                error = string.Empty;
                string value;
                if (repoMetadata.TryGetValue(DiskLayoutVersionKey, out value))
                {
                    if (!int.TryParse(value, out version))
                    {
                        error = "Failed to parse persisted disk layout version number: " + value;
                        return false;
                    }                   
                }
                else
                {
                    error = MissingVersionError;
                    return false;
                }

                return true;
            }

            public static bool CheckDiskLayoutVersion(PersistentDictionary<string, string> repoMetadata, bool allowUpgrade, out string error)
            {
                error = string.Empty;
                int persistedVersionNumber;
                if (TryGetOnDiskLayoutVersion(repoMetadata, out persistedVersionNumber, out error))
                {
                    if (persistedVersionNumber < MinDiskLayoutVersion)
                    {
                        error = string.Format(
                            "Breaking change to GVFS disk layout has been made since cloning. \r\nEnlistment disk layout version: {0} \r\nGVFS disk layout version: {1} \r\nMinimum supported version: {2}",
                            persistedVersionNumber,
                            CurrentDiskLayoutVersion,
                            MinDiskLayoutVersion);

                        return false;
                    }
                    else if (persistedVersionNumber > MaxDiskLayoutVersion)
                    {
                        error = string.Format(
                            "Changes to GVFS disk layout do not allow mounting after downgrade. Try mounting again using a more recent version of GVFS. \r\nEnlistment disk layout version: {0} \r\nGVFS disk layout version: {1}",
                            persistedVersionNumber,
                            CurrentDiskLayoutVersion);

                        return false;
                    }
                    else if (!allowUpgrade && persistedVersionNumber < CurrentDiskLayoutVersion)
                    {
                        error = string.Format(
                            "GVFS disk layout is behind current version. \r\nEnlistment disk layout version: {0} \r\nGVFS disk layout version: {1}",
                            persistedVersionNumber,
                            CurrentDiskLayoutVersion);

                        return false;
                    }

                    return true;
                }

                return false;
            }

            /* TODO: Story 957530 Remove code using GVFS_HEAD with next breaking change. */
            public static bool OnDiskVersionSupportsIndexProjection(PersistentDictionary<string, string> repoMetadata)
            {
                string error;
                int persistedVersionNumber;
                if (TryGetOnDiskLayoutVersion(repoMetadata, out persistedVersionNumber, out error))
                {
                    if (persistedVersionNumber < ProjectGitIndexVersion)
                    {
                        return false;
                    }

                    return true;
                }

                return false;
            }

            public static bool OnDiskVersionUsesAlwaysExclude(PersistentDictionary<string, string> repoMetadata)
            {
                string error;
                int persistedVersionNumber;
                if (TryGetOnDiskLayoutVersion(repoMetadata, out persistedVersionNumber, out error))
                {
                    return persistedVersionNumber >= AlwaysExcludeVersion;
                }

                return false;
            }
        }
    }
}
