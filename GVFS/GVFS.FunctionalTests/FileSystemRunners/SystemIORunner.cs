using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace GVFS.FunctionalTests.FileSystemRunners
{
    public class SystemIORunner : FileSystemRunner
    {
        public override bool SupportsHardlinkCreation
        {
            get { return false; }
        }

        public override bool FileExists(string path)
        {
            return File.Exists(path);
        }

        public override string MoveFile(string sourcePath, string targetPath)
        {
            File.Move(sourcePath, targetPath);
            return string.Empty;
        }

        public override void MoveFileShouldFail(string sourcePath, string targetPath)
        {
            if (Debugger.IsAttached)
            {
                throw new InvalidOperationException("MoveFileShouldFail should not be run with the debugger attached");
            }

            this.ShouldFail<IOException>(() => { this.MoveFile(sourcePath, targetPath); });
        }

        public override void MoveFile_FileShouldNotBeFound(string sourcePath, string targetPath)
        {
            this.ShouldFail<IOException>(() => { this.MoveFile(sourcePath, targetPath); });
        }

        public override string ReplaceFile(string sourcePath, string targetPath)
        {
            File.Replace(sourcePath, targetPath, null);
            return string.Empty;
        }

        public override void ReplaceFile_FileShouldNotBeFound(string sourcePath, string targetPath)
        {
            this.ShouldFail<IOException>(() => { this.ReplaceFile(sourcePath, targetPath); });
        }

        public override string DeleteFile(string path)
        {
            File.Delete(path);
            return string.Empty;
        }

        public override void DeleteFile_FileShouldNotBeFound(string path)
        {
            // Delete file silently succeeds when file is non-existent
            this.DeleteFile(path);
        }

        public override void DeleteFile_AccessShouldBeDenied(string path)
        {
            this.ShouldFail<Exception>(() => { this.DeleteFile(path); });
        }

        public override string ReadAllText(string path)
        {
            return File.ReadAllText(path);
        }

        public override void CreateEmptyFile(string path)
        {
            using (FileStream fs = File.Create(path))
            {
            }
        }

        public override void CreateHardLink(string targetPath, string newLinkPath)
        {
            Assert.Fail($"{nameof(SystemIORunner)} does not support {nameof(this.CreateHardLink)}");
        }

        public override void WriteAllText(string path, string contents)
        {
            File.WriteAllText(path, contents);
        }

        public override void AppendAllText(string path, string contents)
        {
            File.AppendAllText(path, contents);
        }

        public override void WriteAllTextShouldFail<ExceptionType>(string path, string contents)
        {
            if (Debugger.IsAttached)
            {
                throw new InvalidOperationException("WriteAllTextShouldFail should not be run with the debugger attached");
            }

            this.ShouldFail<ExceptionType>(() => { this.WriteAllText(path, contents); });
        }

        public override bool DirectoryExists(string path)
        {
            return Directory.Exists(path);
        }

        public override void MoveDirectory(string sourcePath, string targetPath)
        {
            Directory.Move(sourcePath, targetPath);
        }

        public override void RenameDirectory(string workingDirectory, string source, string target)
        {
            MoveFileEx(Path.Combine(workingDirectory, source), Path.Combine(workingDirectory, target), 0);
        }

        public override void MoveDirectory_RequestShouldNotBeSupported(string sourcePath, string targetPath)
        {
            if (Debugger.IsAttached)
            {
                throw new InvalidOperationException("MoveDirectory_RequestShouldNotBeSupported should not be run with the debugger attached");
            }

            Assert.Catch<IOException>(() => this.MoveDirectory(sourcePath, targetPath));
        }

        public override void MoveDirectory_TargetShouldBeInvalid(string sourcePath, string targetPath)
        {
            if (Debugger.IsAttached)
            {
                throw new InvalidOperationException("MoveDirectory_TargetShouldBeInvalid should not be run with the debugger attached");
            }

            Assert.Catch<IOException>(() => this.MoveDirectory(sourcePath, targetPath));
        }

        public override void CreateDirectory(string path)
        {
            Directory.CreateDirectory(path);
        }

        public override string DeleteDirectory(string path)
        {
            DirectoryInfo directory = new DirectoryInfo(path);

            foreach (FileInfo file in directory.GetFiles())
            {
                file.Attributes = FileAttributes.Normal;

                RetryOnException(() => file.Delete());
            }

            foreach (DirectoryInfo subDirectory in directory.GetDirectories())
            {
                this.DeleteDirectory(subDirectory.FullName);
            }

            RetryOnException(() => directory.Delete());
            return string.Empty;
        }

        public override string EnumerateDirectory(string path)
        {
            return string.Join(Environment.NewLine, Directory.GetFileSystemEntries(path));
        }

        public override void DeleteDirectory_DirectoryShouldNotBeFound(string path)
        {
            this.ShouldFail<IOException>(() => { this.DeleteDirectory(path); });
        }

        public override void DeleteDirectory_ShouldBeBlockedByProcess(string path)
        {
            Assert.Fail("DeleteDirectory_ShouldBeBlockedByProcess not supported by SystemIORunner");
        }

        public override void ReadAllText_FileShouldNotBeFound(string path)
        {
            this.ShouldFail<IOException>(() => { this.ReadAllText(path); });
        }

        [DllImport("kernel32", SetLastError = true)]
        private static extern bool MoveFileEx(string existingFileName, string newFileName, int flags);

        private static void RetryOnException(Action action)
        {
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    action();
                    break;
                }
                catch (IOException)
                {
                    Thread.Sleep(500);
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(500);
                }
            }
        }

        private void ShouldFail<ExceptionType>(Action action) where ExceptionType : Exception
        {
            Assert.Catch<ExceptionType>(() => action());
        }
    }
}
