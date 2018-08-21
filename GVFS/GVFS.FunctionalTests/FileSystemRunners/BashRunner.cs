using GVFS.FunctionalTests.Properties;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace GVFS.FunctionalTests.FileSystemRunners
{
    public class BashRunner : ShellRunner
    {
        private static string[] fileNotFoundMessages = new string[]
        {
            "cannot stat",
            "cannot remove",
            "No such file or directory"
        };

        private static string[] invalidMovePathMessages = new string[]
        {
            "cannot move",
            "No such file or directory"
        };

        private static string[] moveDirectoryNotSupportedMessage = new string[]
        {
            "Function not implemented"
        };

        private static string[] permissionDeniedMessage = new string[]
        {
            "Permission denied"
        };

        private readonly string pathToBash;

        public BashRunner()
        {
            if (File.Exists(Settings.Default.PathToBash))
            {
                this.pathToBash = Settings.Default.PathToBash;
            }
            else
            {
                this.pathToBash = "bash.exe";
            }
        }

        public override bool SupportsHardlinkCreation
        {
            get { return true; }
        }

        protected override string FileName
        {
            get
            {
                return this.pathToBash;
            }
        }

        public static void DeleteDirectoryWithUnlimitedRetries(string path)
        {
            BashRunner runner = new BashRunner();
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
            string bashPath = this.ConvertWinPathToBashPath(path);

            string command = string.Format("-c  \"[ -f {0} ] && echo {1} || echo {2}\"", bashPath, ShellRunner.SuccessOutput, ShellRunner.FailureOutput);

            string output = this.RunProcess(command).Trim();

            return output.Equals(ShellRunner.SuccessOutput, StringComparison.InvariantCulture);
        }

        public override string MoveFile(string sourcePath, string targetPath)
        {
            string sourceBashPath = this.ConvertWinPathToBashPath(sourcePath);
            string targetBashPath = this.ConvertWinPathToBashPath(targetPath);

            return this.RunProcess(string.Format("-c \"mv {0} {1}\"", sourceBashPath, targetBashPath));
        }

        public override void MoveFileShouldFail(string sourcePath, string targetPath)
        {
            // BashRunner does nothing special when a failure is expected, so just confirm source file is still present
            this.MoveFile(sourcePath, targetPath);
            this.FileExists(sourcePath).ShouldEqual(true);
        }

        public override void MoveFile_FileShouldNotBeFound(string sourcePath, string targetPath)
        {
            this.MoveFile(sourcePath, targetPath).ShouldContainOneOf(fileNotFoundMessages);
        }

        public override string ReplaceFile(string sourcePath, string targetPath)
        {
            string sourceBashPath = this.ConvertWinPathToBashPath(sourcePath);
            string targetBashPath = this.ConvertWinPathToBashPath(targetPath);

            return this.RunProcess(string.Format("-c \"mv -f {0} {1}\"", sourceBashPath, targetBashPath));
        }

        public override string DeleteFile(string path)
        {
            string bashPath = this.ConvertWinPathToBashPath(path);

            return this.RunProcess(string.Format("-c \"rm {0}\"", bashPath));
        }

        public override string ReadAllText(string path)
        {
            string bashPath = this.ConvertWinPathToBashPath(path);
            string output = this.RunProcess(string.Format("-c \"cat {0}\"", bashPath));

            // Bash sometimes sticks a trailing "\n" at the end of the output that we need to remove
            // Until we can figure out why we cannot use this runner with files that have trailing newlines
            if (output.Length > 0 &&
                output.Substring(output.Length - 1).Equals("\n", StringComparison.InvariantCultureIgnoreCase) &&
                !(output.Length > 1 &&
                  output.Substring(output.Length - 2).Equals("\r\n", StringComparison.InvariantCultureIgnoreCase)))
            {
                output = output.Remove(output.Length - 1, 1);
            }

            return output;
        }

        public override void AppendAllText(string path, string contents)
        {
            string bashPath = this.ConvertWinPathToBashPath(path);

            this.RunProcess(string.Format("-c \"echo -n \\\"{0}\\\" >> {1}\"", contents, bashPath));
        }

        public override void CreateEmptyFile(string path)
        {
            string bashPath = this.ConvertWinPathToBashPath(path);

            this.RunProcess(string.Format("-c \"touch {0}\"", bashPath));
        }

        public override void CreateHardLink(string existingPath, string newLinkPath)
        {
            string existingFileBashPath = this.ConvertWinPathToBashPath(existingPath);
            string newLinkBashPath = this.ConvertWinPathToBashPath(existingPath);

            this.RunProcess(string.Format("-c \"ln {0} {1}\"", existingFileBashPath, newLinkBashPath));
        }

        public override void WriteAllText(string path, string contents)
        {
            string bashPath = this.ConvertWinPathToBashPath(path);

            this.RunProcess(string.Format("-c \"echo \\\"{0}\\\" > {1}\"", contents, bashPath));
        }

        public override void WriteAllTextShouldFail<ExceptionType>(string path, string contents)
        {
            // BashRunner does nothing special when a failure is expected
            this.WriteAllText(path, contents);
        }

        public override bool DirectoryExists(string path)
        {
            string bashPath = this.ConvertWinPathToBashPath(path);

            string output = this.RunProcess(string.Format("-c  \"[ -d {0} ] && echo {1} || echo {2}\"", bashPath, ShellRunner.SuccessOutput, ShellRunner.FailureOutput)).Trim();

            return output.Equals(ShellRunner.SuccessOutput, StringComparison.InvariantCulture);
        }

        public override void MoveDirectory(string sourcePath, string targetPath)
        {
            this.MoveFile(sourcePath, targetPath);
        }

        public override void RenameDirectory(string workingDirectory, string source, string target)
        {
            this.MoveDirectory(Path.Combine(workingDirectory, source), Path.Combine(workingDirectory, target));
        }

        public override void MoveDirectory_RequestShouldNotBeSupported(string sourcePath, string targetPath)
        {
            this.MoveFile(sourcePath, targetPath).ShouldContain(moveDirectoryNotSupportedMessage);
        }

        public override void MoveDirectory_TargetShouldBeInvalid(string sourcePath, string targetPath)
        {
            this.MoveFile(sourcePath, targetPath).ShouldContainOneOf(invalidMovePathMessages);
        }

        public override void CreateDirectory(string path)
        {
            string bashPath = this.ConvertWinPathToBashPath(path);

            this.RunProcess(string.Format("-c \"mkdir {0}\"", bashPath));
        }

        public override string DeleteDirectory(string path)
        {
            string bashPath = this.ConvertWinPathToBashPath(path);

            return this.RunProcess(string.Format("-c \"rm -rf {0}\"", bashPath));
        }

        public override string EnumerateDirectory(string path)
        {
            string bashPath = this.ConvertWinPathToBashPath(path);

            return this.RunProcess(string.Format("-c \"ls {0}\"", bashPath));
        }

        public override void ReplaceFile_FileShouldNotBeFound(string sourcePath, string targetPath)
        {
            this.ReplaceFile(sourcePath, targetPath).ShouldContainOneOf(fileNotFoundMessages);
        }

        public override void DeleteFile_FileShouldNotBeFound(string path)
        {
            this.DeleteFile(path).ShouldContainOneOf(fileNotFoundMessages);
        }

        public override void DeleteFile_AccessShouldBeDenied(string path)
        {
            this.DeleteFile(path).ShouldContain(permissionDeniedMessage);
        }

        public override void ReadAllText_FileShouldNotBeFound(string path)
        {
            this.ReadAllText(path).ShouldContainOneOf(fileNotFoundMessages);
        }

        public override void DeleteDirectory_DirectoryShouldNotBeFound(string path)
        {
            // Delete directory silently succeeds when deleting a non-existent path
            this.DeleteDirectory(path);
        }

        public override void DeleteDirectory_ShouldBeBlockedByProcess(string path)
        {
            Assert.Fail("Unlike the other runners, bash.exe does not check folder handle before recusively deleting");
        }

        private string ConvertWinPathToBashPath(string winPath)
        {
            string bashPath = string.Concat("/", winPath);
            bashPath = bashPath.Replace(":\\", "/");
            bashPath = bashPath.Replace('\\', '/');
            return bashPath;
        }
    }
}
