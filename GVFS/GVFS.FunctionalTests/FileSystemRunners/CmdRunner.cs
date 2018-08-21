using GVFS.Tests.Should;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace GVFS.FunctionalTests.FileSystemRunners
{
    public class CmdRunner : ShellRunner
    {
        private const string ProcessName = "CMD.exe";

        private static string[] missingFileErrorMessages = new string[]
        {
            "The system cannot find the file specified.",
            "The system cannot find the path specified.",
            "Could Not Find"
        };

        private static string[] moveDirectoryFailureMessage = new string[]
        {
            "0 dir(s) moved"
        };

        private static string[] fileUsedByAnotherProcessMessage = new string[]
        {
            "The process cannot access the file because it is being used by another process"
        };

        public override bool SupportsHardlinkCreation
        {
            get { return true; }
        }

        protected override string FileName
        {
            get
            {
                return ProcessName;
            }
        }

        public static void DeleteDirectoryWithUnlimitedRetries(string path)
        {
            CmdRunner runner = new CmdRunner();
            bool pathExists = Directory.Exists(path);
            int retryCount = 0;
            while (pathExists)
            {
                string output = runner.DeleteDirectory(path);
                pathExists = Directory.Exists(path);
                if (pathExists)
                {
                    ++retryCount;
                    Thread.Sleep(500);
                    if (retryCount > 10)
                    {
                        retryCount = 0;
                        if (Debugger.IsAttached)
                        {
                            Debugger.Break();
                        }
                    }
                }
            }
        }

        public override bool FileExists(string path)
        {
            if (this.DirectoryExists(path))
            {
                return false;
            }

            string output = this.RunProcess(string.Format("/C if exist \"{0}\" (echo {1}) else (echo {2})", path, ShellRunner.SuccessOutput, ShellRunner.FailureOutput)).Trim();  
                  
            return output.Equals(ShellRunner.SuccessOutput, StringComparison.InvariantCulture);
        }

        public override string MoveFile(string sourcePath, string targetPath)
        {
            return this.RunProcess(string.Format("/C move \"{0}\" \"{1}\"", sourcePath, targetPath));
        }

        public override void MoveFileShouldFail(string sourcePath, string targetPath)
        {
            // CmdRunner does nothing special when a failure is expected
            this.MoveFile(sourcePath, targetPath);
        }

        public override void MoveFile_FileShouldNotBeFound(string sourcePath, string targetPath)
        {
            this.MoveFile(sourcePath, targetPath).ShouldContainOneOf(missingFileErrorMessages);
        }

        public override string ReplaceFile(string sourcePath, string targetPath)
        {
            return this.RunProcess(string.Format("/C move /Y \"{0}\" \"{1}\"", sourcePath, targetPath));
        }

        public override string DeleteFile(string path)
        {
            return this.RunProcess(string.Format("/C del \"{0}\"", path));
        }

        public override string ReadAllText(string path)
        {
            return this.RunProcess(string.Format("/C type \"{0}\"", path));
        }

        public override void CreateEmptyFile(string path)
        {
            this.RunProcess(string.Format("/C type NUL > \"{0}\"", path));
        }

        public override void CreateHardLink(string targetPath, string newLinkPath)
        {
            this.RunProcess(string.Format("/C mklink /H \"{0}\" \"{1}\"", newLinkPath, targetPath));
        }

        public override void AppendAllText(string path, string contents)
        {
            // Use echo|set /p with "" to avoid adding any trailing whitespace or newline
            // to the contents
            this.RunProcess(string.Format("/C echo|set /p =\"{0}\" >> {1}", contents, path));
        }

        public override void WriteAllText(string path, string contents)
        {
            // Use echo|set /p with "" to avoid adding any trailing whitespace or newline
            // to the contents
            this.RunProcess(string.Format("/C echo|set /p =\"{0}\" > {1}", contents, path));
        }
        
        public override void WriteAllTextShouldFail<ExceptionType>(string path, string contents)
        {
            // CmdRunner does nothing special when a failure is expected
            this.WriteAllText(path, contents);
        }

        public override bool DirectoryExists(string path)
        {
            string parentDirectory = Path.GetDirectoryName(path);
            string targetName = Path.GetFileName(path);
                  
            string output = this.RunProcess(string.Format("/C dir /A:d /B {0}", parentDirectory));
            string[] directories = output.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string directory in directories)
            {
                if (directory.Equals(targetName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public override void CreateDirectory(string path)
        {
            this.RunProcess(string.Format("/C mkdir \"{0}\"", path));
        }

        public override string DeleteDirectory(string path)
        {
            return this.RunProcess(string.Format("/C rmdir /q /s \"{0}\"", path));
        }

        public override string EnumerateDirectory(string path)
        {
            return this.RunProcess(string.Format("/C dir \"{0}\"", path));
        }

        public override void MoveDirectory(string sourcePath, string targetPath)
        {
            this.MoveFile(sourcePath, targetPath);
        }

        public override void RenameDirectory(string workingDirectory, string source, string target)
        {
            this.RunProcess(string.Format("/C ren \"{0}\" \"{1}\"", source, target), workingDirectory);
        }

        public override void MoveDirectory_RequestShouldNotBeSupported(string sourcePath, string targetPath)
        {
            this.MoveFile(sourcePath, targetPath).ShouldContain(moveDirectoryFailureMessage);
        }

        public override void MoveDirectory_TargetShouldBeInvalid(string sourcePath, string targetPath)
        {
            this.MoveFile(sourcePath, targetPath).ShouldContain(moveDirectoryFailureMessage);
        }

        public string RunCommand(string command)
        {
            return this.RunProcess(string.Format("/C {0}", command));
        }

        public override void ReplaceFile_FileShouldNotBeFound(string sourcePath, string targetPath)
        {
            this.ReplaceFile(sourcePath, targetPath).ShouldContainOneOf(missingFileErrorMessages);
        }

        public override void DeleteFile_FileShouldNotBeFound(string path)
        {
            this.DeleteFile(path).ShouldContainOneOf(missingFileErrorMessages);
        }

        public override void DeleteFile_AccessShouldBeDenied(string path)
        {
            // CMD does not report any error messages when access is denied, so just confirm the file still exists
            this.DeleteFile(path);
            this.FileExists(path).ShouldEqual(true);
        }

        public override void ReadAllText_FileShouldNotBeFound(string path)
        {
            this.ReadAllText(path).ShouldContainOneOf(missingFileErrorMessages);
        }

        public override void DeleteDirectory_DirectoryShouldNotBeFound(string path)
        {
            this.DeleteDirectory(path).ShouldContainOneOf(missingFileErrorMessages);
        }

        public override void DeleteDirectory_ShouldBeBlockedByProcess(string path)
        {
            this.DeleteDirectory(path).ShouldContain(fileUsedByAnotherProcessMessage);
        }
    }
}
