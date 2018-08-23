namespace GVFS.Common.FileSystem
{
    public interface IPlatformFileSystem
    {
        bool SupportsFileMode { get; }
        void FlushFileBuffers(string path);
        void MoveAndOverwriteFile(string sourceFileName, string destinationFilename);
        void CreateHardLink(string newFileName, string existingFileName);
        bool TryGetNormalizedPath(string path, out string normalizedPath, out string errorMessage);
        void ChangeMode(string path, int mode);
    }
}
