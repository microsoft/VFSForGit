using GVFS.Common;
using GVFS.Common.Tracing;
using GVFS.DiskLayoutUpgrades;
using System.IO;

namespace GVFS.Platform.Windows.DiskLayoutUpgrades
{
    public class DiskLayout11to12Upgrade_SharedLocalCache : DiskLayoutUpgrade.MajorUpgrade
    {
        protected override int SourceMajorVersion
        {
            get { return 11; }
        }

        /// <summary>
        /// Version 11 to 12 added the shared local git objects cache.
        /// </summary>
        public override bool TryUpgrade(ITracer tracer, string enlistmentRoot)
        {
            string dotGVFSPath = Path.Combine(enlistmentRoot, GVFSPlatform.Instance.Constants.DotGVFSRoot);
            string error;
            if (!RepoMetadata.TryInitialize(tracer, dotGVFSPath, out error))
            {
                tracer.RelatedError(nameof(this.TryUpgradeGitObjectPath) + ": Could not initialize repo metadata: " + error);
                return false;
            }

            if (!this.TryUpgradeGitObjectPath(tracer, enlistmentRoot))
            {
                return false;
            }

            RepoMetadata.Instance.SetLocalCacheRoot(string.Empty);
            tracer.RelatedInfo("Set LocalCacheRoot to string.Empty");

            if (!this.TryIncrementMajorVersion(tracer, enlistmentRoot))
            {
                return false;
            }

            return true;
        }

        private bool TryUpgradeGitObjectPath(ITracer tracer, string enlistmentRoot)
        {
            string gitObjectsRoot;
            string legacyDotGVFSGitObjectCachePath = Path.Combine(enlistmentRoot, GVFSPlatform.Instance.Constants.DotGVFSRoot, "gitObjectCache");
            if (Directory.Exists(legacyDotGVFSGitObjectCachePath))
            {
                gitObjectsRoot = legacyDotGVFSGitObjectCachePath;
            }
            else
            {
                // Old version prior to <root>\.gvfs\gitObjectCache cache
                gitObjectsRoot = Path.Combine(enlistmentRoot, GVFSConstants.WorkingDirectoryRootName, GVFSConstants.DotGit.Objects.Root);
            }

            RepoMetadata.Instance.SetGitObjectsRoot(gitObjectsRoot);
            tracer.RelatedInfo("Set GitObjectsRoot: " + gitObjectsRoot);
            return true;
        }
    }
}