using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace GVFS.FunctionalTests.FileSystemRunners
{
    public class SystemIORunner : FileSystemRunner
    {
        public override bool FileExists(string path)
        {
            return File.Exists(path);
        }

        public override string MoveFile(string sourcePath, string targetPath)
        {
            File.Move(sourcePath, targetPath);
            return string.Empty;
        }

        public override void CreateFileWithoutClose(string path)
        {
            File.Create(path);
        }

        public override void OpenFileAndWriteWithoutClose(string path, string content)
        {
            StreamWriter file = new StreamWriter(path);
            file.Write(content);
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

        public override void ReplaceFile_AccessShouldBeDenied(string sourcePath, string targetPath)
        {
            this.ShouldFail<Exception>(() => { this.ReplaceFile(sourcePath, targetPath); });
            this.FileExists(sourcePath).ShouldBeTrue($"{sourcePath} does not exist when it should");
            this.FileExists(targetPath).ShouldBeFalse($"{targetPath} exists when it should not");
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
            this.FileExists(path).ShouldBeTrue($"{path} does not exist when it should");
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

        public override void CreateHardLink(string newLinkFilePath, string existingFilePath)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                WindowsCreateHardLink(newLinkFilePath, existingFilePath, IntPtr.Zero).ShouldBeTrue($"Failed to create hard link: {Marshal.GetLastWin32Error()}");
            }
            else
            {
                MacCreateHardLink(existingFilePath, newLinkFilePath).ShouldEqual(0, $"Failed to create hard link: {Marshal.GetLastWin32Error()}");
            }
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
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                MoveFileEx(Path.Combine(workingDirectory, source), Path.Combine(workingDirectory, target), 0);
            }
            else
            {
                Rename(Path.Combine(workingDirectory, source), Path.Combine(workingDirectory, target));
            }
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

        public override void ChangeMode(string path, ushort mode)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new NotSupportedException();
            }
            else
            {
                Chmod(path, mode).ShouldEqual(0, $"Failed to chmod: {Marshal.GetLastWin32Error()}");
            }
        }

        public override long FileSize(string path)
        {
            return new FileInfo(path).Length;
        }

        [DllImport("kernel32", SetLastError = true)]
        private static extern bool MoveFileEx(string existingFileName, string newFileName, int flags);

        [DllImport("libc", EntryPoint = "link", SetLastError = true)]
        private static extern int MacCreateHardLink(string oldPath, string newPath);

        [DllImport("libc", EntryPoint = "chmod", SetLastError = true)]
        private static extern int Chmod(string pathname, ushort mode);

        [DllImport("libc", EntryPoint = "rename", SetLastError = true)]
        private static extern int Rename(string oldPath, string newPath);

        [DllImport("kernel32.dll", EntryPoint = "CreateHardLink", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool WindowsCreateHardLink(
            string newLinkFileName,
            string existingFileName,
            IntPtr securityAttributes);

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
