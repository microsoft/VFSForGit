using GVFS.Common.Git;
using System.IO;

namespace GVFS.Common
{
    public static class GVFSConstants
    {
        public const int ShaStringLength = 40;
        public const int MaxPath = 260;
        public const string AllZeroSha = "0000000000000000000000000000000000000000";

        public const char GitPathSeparator = '/';
        public const string GitPathSeparatorString = "/";
        public const char GitCommentSign = '#';

        public const string PrefetchPackPrefix = "prefetch";

        public const string GVFSEtwProviderName = "Microsoft.Git.GVFS";
        public const string WorkingDirectoryRootName = "src";
        public const string UnattendedEnvironmentVariable = "GVFS_UNATTENDED";

        public const string DefaultGVFSCacheFolderName = ".gvfsCache";

        public const string GitIsNotInstalledError = "Could not find git.exe.  Ensure that Git is installed.";

        /// <summary>
        /// The SupportedGitVersion is a _minimum_ Git version string. This
        /// VFS for Git executable is expected to work consistently with Git
        /// versions that match the platform and revision portions,
        /// as long as the major, minor, and build versions are larger than
        /// this fixed version.
        /// </summary>
        public static readonly GitVersion SupportedGitVersion =
            new GitVersion(2, 30, 0, platform: "vfs", revision: 0, minorRevision: 0);

        public static class GitConfig
        {
            public const string GVFSPrefix = "gvfs.";
            public const string MaxRetriesConfig = GVFSPrefix + "max-retries";
            public const string TimeoutSecondsConfig = GVFSPrefix + "timeout-seconds";
            public const string GitStatusCacheBackoffConfig = GVFSPrefix + "status-cache-backoff-seconds";
            public const string MountId = GVFSPrefix + "mount-id";
            public const string EnlistmentId = GVFSPrefix + "enlistment-id";
            public const string CacheServer = GVFSPrefix + "cache-server";
            public const string DeprecatedCacheEndpointSuffix = ".cache-server-url";
            public const string HooksPrefix = GitConfig.GVFSPrefix + "clone.default-";
            public const string GVFSTelemetryId = GitConfig.GVFSPrefix + "telemetry-id";
            public const string GVFSTelemetryPipe = GitConfig.GVFSPrefix + "telemetry-pipe";
            public const string IKey = GitConfig.GVFSPrefix + "ikey";
            public const string HooksExtension = ".hooks";
        }

        public static class LocalGVFSConfig
        {
            public const string UpgradeRing = "upgrade.ring";
            public const string UpgradeFeedPackageName = "upgrade.feedpackagename";
            public const string UpgradeFeedUrl = "upgrade.feedurl";
            public const string OrgInfoServerUrl = "upgrade.orgInfoServerUrl";
            public const string USNJournalUpdates = "usn.updateDirectories";
        }

        public static class GitStatusCache
        {
            public const string EnableGitStatusCacheTokenFile = "EnableGitStatusCacheToken.dat";
        }

        public static class Service
        {
            public const string ServiceName = "GVFS.Service";
            public const string LogDirectory = "Logs";
            public const string UIName = "GVFS.Service.UI";
        }

        public static class MediaTypes
        {
            public const string PrefetchPackFilesAndIndexesMediaType = "application/x-gvfs-timestamped-packfiles-indexes";
            public const string LooseObjectMediaType = "application/x-git-loose-object";
            public const string CustomLooseObjectsMediaType = "application/x-gvfs-loose-objects";
            public const string PackFileMediaType = "application/x-git-packfile";
        }

        public static class Endpoints
        {
            public const string GVFSConfig = "/gvfs/config";
            public const string GVFSObjects = "/gvfs/objects";
            public const string GVFSPrefetch = "/gvfs/prefetch";
            public const string GVFSSizes = "/gvfs/sizes";
            public const string InfoRefs = "/info/refs?service=git-upload-pack";
        }

        public static class SpecialGitFiles
        {
            public const string GitAttributes = ".gitattributes";
            public const string GitIgnore = ".gitignore";
        }

        public static class LogFileTypes
        {
            public const string MountPrefix = "mount";
            public const string UpgradePrefix = "productupgrade";

            public const string Clone = "clone";
            public const string Dehydrate = "dehydrate";
            public const string MountVerb = MountPrefix + "_verb";
            public const string MountProcess = MountPrefix + "_process";
            public const string MountUpgrade = MountPrefix + "_repoupgrade";
            public const string Prefetch = "prefetch";
            public const string Repair = "repair";
            public const string Service = "service";
            public const string ServiceUI = "service_ui";
            public const string Sparse = "sparse";
            public const string UpgradeVerb = UpgradePrefix + "_verb";
            public const string UpgradeProcess = UpgradePrefix + "_process";
            public const string UpgradeSystemInstaller = UpgradePrefix + "_system_installer";
        }

        public static class DotGVFS
        {
            public const string CorruptObjectsName = "CorruptObjects";
            public const string LogName = "logs";

            public static class Databases
            {
                public const string Name = "databases";

                public static readonly string BackgroundFileSystemTasks = Path.Combine(Name, "BackgroundGitOperations.dat");
                public static readonly string PlaceholderList = Path.Combine(Name, "PlaceholderList.dat");
                public static readonly string ModifiedPaths = Path.Combine(Name, "ModifiedPaths.dat");
                public static readonly string RepoMetadata = Path.Combine(Name, "RepoMetadata.dat");
                public static readonly string VFSForGit = Path.Combine(Name, "VFSForGit.sqlite");
            }

            public static class GitStatusCache
            {
                public const string Name = "gitStatusCache";
                public static readonly string CachePath = Path.Combine(Name, "GitStatusCache.dat");
            }
        }

        public static class DotGit
        {
            public const string Root = ".git";
            public const string HeadName = "HEAD";
            public const string IndexName = "index";
            public const string PackedRefsName = "packed-refs";
            public const string LockExtension = ".lock";

            public static readonly string Config = Path.Combine(DotGit.Root, "config");
            public static readonly string Head = Path.Combine(DotGit.Root, HeadName);
            public static readonly string BisectStart = Path.Combine(DotGit.Root, "BISECT_START");
            public static readonly string CherryPickHead = Path.Combine(DotGit.Root, "CHERRY_PICK_HEAD");
            public static readonly string MergeHead = Path.Combine(DotGit.Root, "MERGE_HEAD");
            public static readonly string RevertHead = Path.Combine(DotGit.Root, "REVERT_HEAD");
            public static readonly string RebaseApply = Path.Combine(DotGit.Root, "rebase_apply");
            public static readonly string Index = Path.Combine(DotGit.Root, IndexName);
            public static readonly string IndexLock = Path.Combine(DotGit.Root, IndexName + LockExtension);
            public static readonly string PackedRefs = Path.Combine(DotGit.Root, PackedRefsName);
            public static readonly string Shallow = Path.Combine(DotGit.Root, "shallow");

            public static class Logs
            {
                public static readonly string HeadName = "HEAD";

                public static readonly string Root = Path.Combine(DotGit.Root, "logs");
                public static readonly string Head = Path.Combine(Logs.Root, Logs.HeadName);
            }

            public static class Hooks
            {
                public const string LoaderExecutable = "GitHooksLoader.exe";
                public const string PreCommandHookName = "pre-command";
                public const string PostCommandHookName = "post-command";
                public const string ReadObjectName = "read-object";
                public const string VirtualFileSystemName = "virtual-filesystem";
                public const string PostIndexChangedName = "post-index-change";
                public static readonly string Root = Path.Combine(DotGit.Root, "hooks");
                public static readonly string PreCommandPath = Path.Combine(Hooks.Root, PreCommandHookName);
                public static readonly string PostCommandPath = Path.Combine(Hooks.Root, PostCommandHookName);
                public static readonly string ReadObjectPath = Path.Combine(Hooks.Root, ReadObjectName);
                public static readonly string VirtualFileSystemPath = Path.Combine(Hooks.Root, VirtualFileSystemName);
                public static readonly string PostIndexChangedPath = Path.Combine(Hooks.Root, PostIndexChangedName);
            }

            public static class Info
            {
                public const string Name = "info";
                public const string ExcludeName = "exclude";
                public const string AlwaysExcludeName = "always_exclude";
                public const string SparseCheckoutName = "sparse-checkout";

                public static readonly string Root = Path.Combine(DotGit.Root, Info.Name);
                public static readonly string SparseCheckoutPath = Path.Combine(Info.Root, Info.SparseCheckoutName);
                public static readonly string ExcludePath = Path.Combine(Info.Root, ExcludeName);
                public static readonly string AlwaysExcludePath = Path.Combine(Info.Root, AlwaysExcludeName);
            }

            public static class Objects
            {
                public static readonly string Root = Path.Combine(DotGit.Root, "objects");

                public static class Info
                {
                    public static readonly string Root = Path.Combine(Objects.Root, "info");
                    public static readonly string Alternates = Path.Combine(Info.Root, "alternates");
                }

                public static class Pack
                {
                    public static readonly string Name = "pack";
                    public static readonly string Root = Path.Combine(Objects.Root, Name);
                }
            }

            public static class Refs
            {
                public static readonly string Root = Path.Combine(DotGit.Root, "refs");

                public static class Heads
                {
                    public static readonly string Root = Path.Combine(DotGit.Refs.Root, "heads");
                    public static readonly string RootFolder = Heads.Root + Path.DirectorySeparatorChar;
                }
            }
        }

        public static class InstallationCapabilityFiles
        {
            public const string OnDiskVersion16CapableInstallation = "OnDiskVersion16CapableInstallation.dat";
        }

        public static class VerbParameters
        {
                public const string InternalUseOnly = "internal_use_only";

            public static class Mount
            {
                public const string StartedByService = "StartedByService";
                public const string StartedByVerb = "StartedByVerb";
                public const string Verbosity = "verbosity";
                public const string Keywords = "keywords";
                public const string DebugWindow = "debug-window";

                public const string DefaultVerbosity = "Informational";
                public const string DefaultKeywords = "Any";
            }

            public static class Unmount
            {
                public const string SkipLock = "skip-wait-for-lock";
            }
        }

        public static class UpgradeVerbMessages
        {
            public const string GVFSUpgrade = "`gvfs upgrade`";
            public const string GVFSUpgradeDryRun = "`gvfs upgrade --dry-run`";
            public const string NoUpgradeCheckPerformed = "No upgrade check was performed.";
            public const string NoneRingConsoleAlert = "Upgrade ring set to \"None\". " + NoUpgradeCheckPerformed;
            public const string NoRingConfigConsoleAlert = "Upgrade ring is not set. " + NoUpgradeCheckPerformed;
            public const string InvalidRingConsoleAlert = "Upgrade ring set to unknown value. " + NoUpgradeCheckPerformed;
            public const string SetUpgradeRingCommand = "To set or change upgrade ring, run `gvfs config " + LocalGVFSConfig.UpgradeRing + " [\"Fast\"|\"Slow\"|\"None\"]` from a command prompt.";
            public const string UnmountRepoWarning = "Upgrade will unmount and remount gvfs repos, ensure you are at a stopping point.";
        }
    }
}
