namespace GVFS.FunctionalTests
{
    public static class Categories
    {
        public const string ExtraCoverage = "ExtraCoverage";
        public const string FastFetch = "FastFetch";
        public const string GitCommands = "GitCommands";

        public const string CaseInsensitiveFileSystemOnly = "CaseInsensitiveFileSystemOnly";
        public const string CaseSensitiveFileSystemOnly = "CaseSensitiveFileSystemOnly";
        public const string FileSystemSupportsFileMode = "FileSystemSupportsFileMode";
        public const string RepositoryMountsDifferentFileSystem = "RepositoryMountsDifferentFileSystem";
        public const string RepositoryMountsSameFileSystem = "RepositoryMountsSameFileSystem";

        public const string WindowsOnly = "WindowsOnly";
        public const string LinuxOnly = "LinuxOnly";
        public const string MacOnly = "MacOnly";

        public static class LinuxTODO
        {
            // Tests that fail due to flock() contention with libprojfs
            public const string NeedsContentionFreeFileLock = "NeedsNonContendedFileLock";

            // Tests that fail due to FUSE passthrough write buffering behavior
            public const string NeedsConsistentBufferedWrites = "NeedsConsistentBufferedWrites";
        }

        public static class MacTODO
        {
            // Tests that require #360 (detecting/handling new empty folders)
            public const string NeedsNewFolderCreateNotification = "NeedsNewFolderCreateNotification";

            // Tests that require the Status Cache to be built
            public const string NeedsStatusCache = "NeedsStatusCache";

            // Tests that require Config to be built
            public const string NeedsGVFSConfig = "NeedsConfig";

            // Tests that require VFS Service
            public const string NeedsServiceVerb = "NeedsServiceVerb";

            // Tests requires code updates so that we lock the file instead of looking for a .lock file
            public const string TestNeedsToLockFile = "TestNeedsToLockFile";
        }
    }
}
