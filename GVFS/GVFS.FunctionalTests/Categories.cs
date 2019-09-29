namespace GVFS.FunctionalTests
{
    public static class Categories
    {
        public const string ExtraCoverage = "ExtraCoverage";
        public const string FastFetch = "FastFetch";
        public const string GitCommands = "GitCommands";

        // Linux uses a separate device mount for its repository, and so is unable to rename(2) inodes
        // in or out of the repository filesystem; attempts to do so fail with errno set to EXDEV.
        // Therefore, tests which move files or directories across the repository boundary should
        // be flagged with this category so they will be excluded on Linux.
        public const string RepositoryMountsSameFileSystem = "RepositoryMountsSameFileSystem";

        public const string PartialFolderRenamesAllowed = "PartialFolderRenamesAllowed";
        public const string PartialFolderRenamesBlocked = "PartialFolderRenamesBlocked";

        public const string WindowsOnly = "WindowsOnly";
        public const string LinuxOnly = "LinuxOnly";
        public const string MacOnly = "MacOnly";
        public const string POSIXOnly = "POSIXOnly";

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
        }

        public static class NonWindowsTODO
        {
            // Tests that require the Status Cache to be built
            public const string NeedsStatusCache = "NeedsStatusCache";

            // Tests that require Config to be built
            public const string NeedsGVFSConfig = "NeedsConfig";

            // Tests requires code updates so that we lock the file instead of looking for a .lock file
            public const string TestNeedsToLockFile = "TestNeedsToLockFile";
        }
    }
}
