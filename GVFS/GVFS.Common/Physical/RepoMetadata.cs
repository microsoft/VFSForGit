using GVFS.Common.Tracing;
using Microsoft.Isam.Esent.Collections.Generic;
using System;
using System.IO;

namespace GVFS.Common.Physical
{
    public class RepoMetadata : IDisposable
    {
        private PersistentDictionary<string, string> repoMetadata;

        public RepoMetadata(string dotGVFSPath)
        {
            this.repoMetadata = new PersistentDictionary<string, string>(
                Path.Combine(dotGVFSPath, GVFSConstants.DatabaseNames.RepoMetadata));
        }

        public static int GetCurrentDiskLayoutVersion()
        {
            return DiskLayoutVersion.CurrentDiskLayoutVerion;
        }

        public static bool CheckDiskLayoutVersion(string dotGVFSPath, out string error)
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
                    return repoMetadata.CheckDiskLayoutVersion(out error);
                }
            }
            catch (Exception e)
            {
                error = "Failed to check disk layout version of enlistment, Exception: " + e.ToString();
                return false;
            }
        }

        public void SaveCurrentDiskLayoutVersion()
        {
            DiskLayoutVersion.SaveCurrentDiskLayoutVersion(this.repoMetadata);
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

        private bool CheckDiskLayoutVersion(out string error)
        {
            return DiskLayoutVersion.CheckDiskLayoutVersion(this.repoMetadata, out error);
        }

        private static class DiskLayoutVersion
        {
            // The current disk layout version.  This number should be bumped whenever a disk format change is made
            // that would impact and older GVFS's ability to mount the repo
            public const int CurrentDiskLayoutVerion = 3;

            public const string MissingVersionError = "Enlistment disk layout version not found, check if a breaking change has been made to GVFS since cloning this enlistment.";

            private const string DiskLayoutVersionKey = "DiskLayoutVersion";

            // MaxDiskLayoutVersion ensures that olders versions of GVFS will not try to mount newer enlistments (if the 
            // disk layout of the newer GVFS is incompatible).
            // GVFS will only mount if the disk layout version of the repo is <= MaxDiskLayoutVersion
            private const int MaxDiskLayoutVersion = CurrentDiskLayoutVerion;

            // MinDiskLayoutVersion ensures that GVFS will not attempt to mount an older repo if there has been a breaking format
            // change since that enlistment was cloned.
            //     - GVFS will only mount if the disk layout version of the repo is >= MinDiskLayoutVersion
            //     - Bump this version number only when a breaking change is being made (i.e. upgrade is not supported)
            private const int MinDiskLayoutVersion = 3;

            public static void SaveCurrentDiskLayoutVersion(PersistentDictionary<string, string> repoMetadata)
            {
                repoMetadata[DiskLayoutVersionKey] = CurrentDiskLayoutVerion.ToString();
                repoMetadata.Flush();
            }

            public static bool CheckDiskLayoutVersion(PersistentDictionary<string, string> repoMetadata, out string error)
            {
                error = string.Empty;
                string value;
                if (repoMetadata.TryGetValue(DiskLayoutVersionKey, out value))
                {
                    int persistedVersionNumber;
                    if (!int.TryParse(value, out persistedVersionNumber))
                    {
                        error = "Failed to parse persisted disk layout version number";
                        return false;
                    }

                    if (persistedVersionNumber < MinDiskLayoutVersion)
                    {
                        error = string.Format(
                            "Breaking change to GVFS disk layout has been made since cloning. \r\nEnlistment disk layout version: {0} \r\nGVFS disk layout version: {1} \r\nMinimum supported version: {2}",
                            persistedVersionNumber,
                            CurrentDiskLayoutVerion,
                            MinDiskLayoutVersion);

                        return false;
                    }
                    else if (persistedVersionNumber > MaxDiskLayoutVersion)
                    {
                        error = string.Format(
                            "Changes to GVFS disk layout do not allow mounting after downgrade. Try mounting again using a more recent version of GVFS. \r\nEnlistment disk layout version: {0} \r\nGVFS disk layout version: {1}",
                            persistedVersionNumber,
                            CurrentDiskLayoutVerion);
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
        }
    }
}
