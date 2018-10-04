namespace GVFS.Common.FileSystem
{
    public interface IPlatformFileSystem
    {
        bool SupportsFileMode { get; }
        void FlushFileBuffers(string path);
        void MoveAndOverwriteFile(string sourceFileName, string destinationFilename);
        void CreateHardLink(string newLinkFileName, string existingFileName);
        bool TryGetNormalizedPath(string path, out string normalizedPath, out string errorMessage);
        void ChangeMode(string path, int mode);

        /// <summary>
        /// Check if a path exists as a subpath of the specified directory.
        /// </summary>
        /// <param name="directoryPath">Directory path.</param>
        /// <param name="path">Path to query.</param>
        /// <returns>True if <see cref="path"/> exists as a subpath of <see cref="directoryPath"/>, false otherwise.</returns>
        bool IsPathUnderDirectory(string directoryPath, string path);

        /// <summary>
        /// Get the path to the volume root that the given path.
        /// </summary>
        /// <param name="path">File path to find the volume for.</param>
        /// <returns>Path to the root of the volume.</returns>
        string GetVolumeRoot(string path);

        /// <summary>
        /// Check if the volume for the given path is available and ready for use.
        /// </summary>
        /// <param name="path">Path to any directory or file on a volume.</param>
        /// <remarks>
        /// A volume might be unavailable for multiple reasons, including:
        /// <para/>
        ///  - the volume resides on a removable device which is not present
        /// <para/>
        ///  - the volume is not mounted in the operating system
        /// <para/>
        ///  - the volume is encrypted or locked
        /// </remarks>
        /// <returns>True if the volume is available, false otherwise.</returns>
        bool IsVolumeAvailable(string path);

        /// <summary>
        /// Create an <see cref="IVolumeStateWatcher"/> which monitors for changes to the state of volumes on the system.
        /// </summary>
        /// <returns>Volume watcher</returns>
        IVolumeStateWatcher CreateVolumeStateWatcher();
    }
}
