using GVFS.Common.Git;
using System.IO;

namespace GVFS.Common
{
    public static class GVFSConstants
    {
        public const int ShaStringLength = 40;

        public const string RootFolderPath = "\\";
        public const char PathSeparator = '\\';
        public const string PathSeparatorString = "\\";
        public const char GitPathSeparator = '/';
        public const string GitPathSeparatorString = "/";

        public const string AppName = "GitVirtualFileSystem";
        public const string AppGuid = "9a3cf8bb-ef4b-42df-ac4b-f5f50d114909";

        public const string DotGVFSPath = ".gvfs";
        public const string GVFSLogFolderName = "logs";
        public const string VolumeLabel = "Git Virtual File System";
        
        public const string GVFSConfigEndpointSuffix = "/gvfs/config";
        public const string InfoRefsEndpointSuffix = "/info/refs?service=git-upload-pack";

        public const string VirtualizeObjectsGitConfigName = "core.virtualizeobjects";

        public const string CatFileObjectTypeCommit = "commit";

        public const string PrefetchPackPrefix = "prefetch";

        public const string HeadCommitName = "HEAD";
        public const string GVFSHeadCommitName = "GVFS_HEAD";

        public const string AllZeroSha = "0000000000000000000000000000000000000000";

        public const string GVFSEtwProviderName = "Microsoft.Git.GVFS";

        public const string WorkingDirectoryRootName = "src";

        public const string GVFSHooksExecutableName = "GVFS.Hooks.exe";
        public const string GVFSReadObjectHookExecutableName = "GVFS.ReadObjectHook.exe";
        public const int InvalidProcessId = -1;
        
        public const string GitIsNotInstalledError = "Could not find git.exe.  Ensure that Git is installed.";

        public static readonly GitVersion MinimumGitVersion = new GitVersion(2, 11, 0, "gvfs", 1, 3);

        public static class MediaTypes
        {
            public const string PrefetchPackFilesAndIndexesMediaType = "application/x-gvfs-timestamped-packfiles-indexes";
            public const string LooseObjectMediaType = "application/x-git-loose-object";
            public const string CustomLooseObjectsMediaType = "application/x-gvfs-loose-objects";
            public const string PackFileMediaType = "application/x-git-packfile";
        }

        public static class DatabaseNames
        {
            public const string BackgroundGitUpdates = "BackgroundGitUpdates";
            public const string BlobSizes = "BlobSizes";
            public const string DoNotProject = "DoNotProject";
            public const string RepoMetadata = "RepoMetadata";
        }

        public static class SpecialGitFiles
        {
            public const string GitAttributes = ".gitattributes";
            public const string GitIgnore = ".gitignore";
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
            public static readonly string Index = Path.Combine(DotGit.Root, IndexName);
            public static readonly string PackedRefs = Path.Combine(DotGit.Root, PackedRefsName);
            public static readonly string Shallow = Path.Combine(DotGit.Root, "shallow");

            public static class Logs
            {
                public const string Name = "logs";
                public static readonly string HeadName = "HEAD";

                public static readonly string Root = Path.Combine(DotGit.Root, Name);                
                public static readonly string Head = Path.Combine(Logs.Root, Logs.HeadName);
            }

            public static class Hooks
            {
                public static readonly string ReadObjectName = "read-object";

                public static readonly string Root = Path.Combine(DotGit.Root, "hooks");
                public static readonly string PreCommandPath = Path.Combine(Hooks.Root, "pre-command");
                public static readonly string ReadObjectPath = Path.Combine(Hooks.Root, ReadObjectName);
            }

            public static class Info
            {
                public const string ExcludeName = "exclude";

                public static readonly string Root = Path.Combine(DotGit.Root, "info");
                public static readonly string SparseCheckoutPath = Path.Combine(Info.Root, "sparse-checkout");
                public static readonly string ExcludePath = Path.Combine(Info.Root, ExcludeName);
            }

            public static class Refs
            {
                public const string Name = "refs";

                public static readonly string Root = Path.Combine(DotGit.Root, Refs.Name);

                public static class Heads
                {
                    public const string Name = "heads";
                    public static readonly string Root = Path.Combine(DotGit.Refs.Root, Heads.Name);
                }
            }

            public static class Objects
            {
                public const string Name = "objects";
                public static readonly string Root = Path.Combine(DotGit.Root, Objects.Name);

                public static class Pack
                {
                    public const string Name = "pack";
                    public static readonly string Root = Path.Combine(Objects.Root, Pack.Name);
                }

                public static class Info
                {
                    public static readonly string Root = Path.Combine(Objects.Root, "info");
                }
            }
        }
    }
}
