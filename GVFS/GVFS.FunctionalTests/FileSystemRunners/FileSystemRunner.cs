using NUnit.Framework;
using System;

namespace GVFS.FunctionalTests.FileSystemRunners
{
    public abstract class FileSystemRunner
    {
        private static FileSystemRunner defaultRunner = new SystemIORunner();

        public static object[] AllWindowsRunners { get; } =
            new[]
            {
                new object[] { new SystemIORunner() },
                new object[] { new CmdRunner() },
                new object[] { new PowerShellRunner() },
                new object[] { new BashRunner() },
            };

        public static object[] AllPOSIXRunners { get; } =
            new[]
            {
                new object[] { new SystemIORunner() },
                new object[] { new BashRunner() },
            };

        public static object[] DefaultRunners { get; } =
            new[]
            {
                new object[] { defaultRunner }
            };

        public static object[] Runners
        {
            get { return GVFSTestConfig.FileSystemRunners; }
        }

        /// <summary>
        /// Default runner to use (for tests that do not need to be run with multiple runners)
        /// </summary>
        public static FileSystemRunner DefaultRunner
        {
            get { return defaultRunner; }
        }

        // File methods
        public abstract bool FileExists(string path);
        public abstract string MoveFile(string sourcePath, string targetPath);

        /// <summary>
        /// Attempt to move the specified file to the specifed target path.  By calling this method the caller is
        /// indicating that they expect the move to fail. However, the caller is responsible for verifying that
        /// the move failed.
        /// </summary>
        /// <param name="sourcePath">Path to existing file</param>
        /// <param name="targetPath">Path to target file (target of the move)</param>
        public abstract void MoveFileShouldFail(string sourcePath, string targetPath);
        public abstract void MoveFile_FileShouldNotBeFound(string sourcePath, string targetPath);
        public abstract string ReplaceFile(string sourcePath, string targetPath);
        public abstract void ReplaceFile_FileShouldNotBeFound(string sourcePath, string targetPath);
        public abstract void ReplaceFile_AccessShouldBeDenied(string sourcePath, string targetPath);
        public abstract string DeleteFile(string path);
        public abstract void DeleteFile_FileShouldNotBeFound(string path);
        public abstract void DeleteFile_AccessShouldBeDenied(string path);
        public abstract string ReadAllText(string path);
        public abstract void ReadAllText_FileShouldNotBeFound(string path);

        public abstract void CreateEmptyFile(string path);
        public abstract void CreateHardLink(string newLinkFilePath, string existingFilePath);
        public abstract void ChangeMode(string path, ushort mode);

        /// <summary>
        /// Write the specified contents to the specified file.  By calling this method the caller is
        /// indicating that they expect the write to succeed. However, the caller is responsible for verifying that
        /// the write succeeded.
        /// </summary>
        /// <param name="path">Path to file</param>
        /// <param name="contents">File contents</param>
        public abstract void WriteAllText(string path, string contents);
        public abstract void CreateFileWithoutClose(string path);
        public abstract void OpenFileAndWriteWithoutClose(string path, string data);

        /// <summary>
        /// Append the specified contents to the specified file.  By calling this method the caller is
        /// indicating that they expect the write to succeed. However, the caller is responsible for verifying that
        /// the write succeeded.
        /// </summary>
        /// <param name="path">Path to file</param>
        /// <param name="contents">File contents</param>
        public abstract void AppendAllText(string path, string contents);

        /// <summary>
        /// Attempt to write the specified contents to the specified file.  By calling this method the caller is
        /// indicating that they expect the write to fail. However, the caller is responsible for verifying that
        /// the write failed.
        /// </summary>
        /// <typeparam name="ExceptionType">Expected type of exception to be thrown</typeparam>
        /// <param name="path">Path to file</param>
        /// <param name="contents">File contents</param>
        public abstract void WriteAllTextShouldFail<ExceptionType>(string path, string contents) where ExceptionType : Exception;

        // Directory methods
        public abstract bool DirectoryExists(string path);
        public abstract void MoveDirectory(string sourcePath, string targetPath);
        public abstract void RenameDirectory(string workingDirectory, string source, string target);
        public abstract void MoveDirectory_RequestShouldNotBeSupported(string sourcePath, string targetPath);
        public abstract void MoveDirectory_TargetShouldBeInvalid(string sourcePath, string targetPath);
        public abstract void CreateDirectory(string path);
        public abstract string EnumerateDirectory(string path);
        public abstract long FileSize(string path);

        /// <summary>
        /// A recursive delete of a directory
        /// </summary>
        public abstract string DeleteDirectory(string path);
        public abstract void DeleteDirectory_DirectoryShouldNotBeFound(string path);
        public abstract void DeleteDirectory_ShouldBeBlockedByProcess(string path);
    }
}
