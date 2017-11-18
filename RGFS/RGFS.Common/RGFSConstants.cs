using RGFS.Common.Git;
using System.IO;

namespace RGFS.Common
{
    public static partial class RGFSConstants
    {
        public const int ShaStringLength = 40;
        public const int MaxPath = 260;
        public const string AllZeroSha = "0000000000000000000000000000000000000000";

        public const char PathSeparator = '\\';
        public const string PathSeparatorString = "\\";
        public const char GitPathSeparator = '/';
        public const string GitPathSeparatorString = "/";
        public const char GitCommentSign = '#';

        public const string PrefetchPackPrefix = "prefetch";

        public const string RGFSEtwProviderName = "Microsoft.Git.RGFS";
        public const string WorkingDirectoryRootName = "src";
        public const string UnattendedEnvironmentVariable = "RGFS_UNATTENDED";

        public const string RGFSExecutableName = "RGFS.exe";
        public const string RGFSHooksExecutableName = "RGFS.Hooks.exe";
        public const string RGFSReadObjectHookExecutableName = "RGFS.ReadObjectHook.exe";
        public const string MountExecutableName = "RGFS.Mount.exe";
        public const string ExecutableExtension = ".exe";
        public const string GitIsNotInstalledError = "Could not find git.exe.  Ensure that Git is installed.";

        public static readonly GitVersion MinimumGitVersion = new GitVersion(2, 15, 0, "rgfs", 1, 0);

        public static class GitConfig
        {
            public const string RGFSPrefix = "rgfs.";
        }

        public static class Service
        {
            public const string ServiceName = "RGFS.Service";
            public const string UIName = "RGFS.Service.UI";
        }

        public static class MediaTypes
        {
            public const string PrefetchPackFilesAndIndexesMediaType = "application/x-rgfs-timestamped-packfiles-indexes";
            public const string LooseObjectMediaType = "application/x-git-loose-object";
            public const string CustomLooseObjectsMediaType = "application/x-rgfs-loose-objects";
            public const string PackFileMediaType = "application/x-git-packfile";
        }

        public static class Endpoints
        {
            public const string RGFSConfig = "/rgfs/config";
            public const string RGFSObjects = "/rgfs/objects";
            public const string RGFSPrefetch = "/rgfs/prefetch";
            public const string RGFSSizes = "/rgfs/sizes";
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

            public const string Clone = "clone";
            public const string Dehydrate = "dehydrate";
            public const string MountVerb = MountPrefix + "_verb";
            public const string MountProcess = MountPrefix + "_process";
            public const string Prefetch = "prefetch";
            public const string Repair = "repair";
            public const string Service = "service";
            public const string Upgrade = MountPrefix + "_upgrade";
        }

        public static class DotRGFS
        {
            public const string Root = ".rgfs";
            public const string CorruptObjectsName = "CorruptObjects";

            public static readonly string LogPath = Path.Combine(DotRGFS.Root, "logs");
            public static readonly string GitObjectCachePath = Path.Combine(DotRGFS.Root, "gitObjectCache");
            public static readonly string CorruptObjectsPath = Path.Combine(DotRGFS.Root, CorruptObjectsName);

            public static readonly string BlobSizesName = "BlobSizes";

            public static class Databases
            {
                public const string Name = "databases";

                public static readonly string BackgroundGitOperations = Path.Combine(Name, "BackgroundGitOperations.dat");
                public static readonly string PlaceholderList = Path.Combine(Name, "PlaceholderList.dat");
                public static readonly string RepoMetadata = Path.Combine(Name, "RepoMetadata.dat");
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
                public const string ConfigExtension = ".hooks";
                public const string ConfigNamePrefix = "rgfs.clone.default-";
                public const string LoaderExecutable = "GitHooksLoader.exe";
                public const string PreCommandHookName = "pre-command";
                public const string PostCommandHookName = "post-command";
                public static readonly string ReadObjectName = "read-object";
                public static readonly string Root = Path.Combine(DotGit.Root, "hooks");
                public static readonly string PreCommandPath = Path.Combine(Hooks.Root, PreCommandHookName);
                public static readonly string PostCommandPath = Path.Combine(Hooks.Root, PostCommandHookName);
                public static readonly string ReadObjectPath = Path.Combine(Hooks.Root, ReadObjectName);
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
                }
            }
        }

        public static class VerbParameters
        {
            public static class Mount
            {
                public const string ServiceName = "internal_use_only_service_name";
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
    }
}
