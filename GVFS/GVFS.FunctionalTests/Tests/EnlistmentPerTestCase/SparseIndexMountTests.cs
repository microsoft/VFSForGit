using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerTestCase
{
    [TestFixture]
    public class SparseIndexMountTests : TestsWithEnlistmentPerTestCase
    {
        private const string BypassGVFS = "-c core.virtualfilesystem= -c core.hookspath=";
        private const string ConeMode = "-c core.sparseCheckout=true -c core.sparseCheckoutCone=true";

        [TestCase]
        public void MountRejectsSparseIndexAndRecoveryStepsWork()
        {
            string fullIndexEntries = GitProcess.Invoke(this.Enlistment.RepoRoot, "ls-files -s");

            this.Enlistment.UnmountGVFS();

            // Turn on the sparse index in the repository config, and collapse every top-level
            // folder into a sparse-directory entry. The persisted projection gets the same index,
            // as it does when git collapses the index while the repository is mounted.
            this.GitShouldSucceed("-c core.hookspath= config index.sparse true");
            string sparseCheckoutPath = this.Enlistment.GetDotGitPath("info", "sparse-checkout");
            string originalSparseCheckout = File.Exists(sparseCheckoutPath) ? File.ReadAllText(sparseCheckoutPath) : null;
            File.WriteAllText(sparseCheckoutPath, "/*\n!/*/\n");
            this.GitShouldSucceed($"{BypassGVFS} {ConeMode} -c sparse.expectFilesOutsideOfPatterns=true update-index --force-write-index");
            string[] collapsedFolders = this.GitShouldSucceed($"{BypassGVFS} {ConeMode} ls-files --sparse")
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.EndsWith("/"))
                .ToArray();
            collapsedFolders.ShouldBeNonEmpty();
            if (originalSparseCheckout == null)
            {
                File.Delete(sparseCheckoutPath);
            }
            else
            {
                File.WriteAllText(sparseCheckoutPath, originalSparseCheckout);
            }

            File.Copy(this.Enlistment.GetDotGitPath("index"), Path.Combine(this.Enlistment.DotGVFSRoot, "GVFS_projection"), overwrite: true);

            this.Enlistment.TryMountGVFS(out string output).ShouldBeFalse("Mount must fail for a sparse index");
            output.ShouldContain("The git index contains the sparse-directory entry", "1. If the enlistment is mounted, run 'gvfs unmount'.", "5. Run 'gvfs mount'.");
            output.ShouldNotContain(ignoreCase: false, "InvalidDataException", "SparseIndexNotSupportedException", "   at GVFS.");

            // The mount failed, so step 1 does not apply. Do steps 2, 3, and 4 as printed.
            Match configStep = Regex.Match(output, @"2\. In '(?<directory>[^']+)', run 'git (?<arguments>[^']+)'\.");
            configStep.Success.ShouldBeTrue("Output does not contain step 2: " + output);
            Match expandStep = Regex.Match(output, @"3\. In '(?<directory>[^']+)', run 'git (?<arguments>[^']+)'\.");
            expandStep.Success.ShouldBeTrue("Output does not contain step 3: " + output);
            Match deleteStep = Regex.Match(output, @"4\. Delete '(?<file>[^']+)'\.");
            deleteStep.Success.ShouldBeTrue("Output does not contain step 4: " + output);

            this.GitShouldSucceed(configStep.Groups["arguments"].Value, configStep.Groups["directory"].Value);
            this.GitShouldSucceed(expandStep.Groups["arguments"].Value, expandStep.Groups["directory"].Value);
            File.Exists(deleteStep.Groups["file"].Value).ShouldBeTrue("Step 4 names a file that does not exist: " + deleteStep.Groups["file"].Value);
            File.Delete(deleteStep.Groups["file"].Value);

            this.Enlistment.MountGVFS();

            this.GitShouldSucceed("config index.sparse").Trim().ShouldEqual("false");
            GitProcess.Invoke(this.Enlistment.RepoRoot, "ls-files -s").ShouldEqual(fullIndexEntries);
            string fileInCollapsedFolder = fullIndexEntries
                .Split('\n')
                .Select(line => line.Split('\t').Last().Trim())
                .First(path => collapsedFolders.Any(folder => path.StartsWith(folder)));
            File.Exists(this.Enlistment.GetVirtualPathTo(fileInCollapsedFolder)).ShouldBeTrue("Projection is missing " + fileInCollapsedFolder);
        }

        [TestCase]
        public void MountExpandsSparseIndexWhenSparseIndexSupportIsOn()
        {
            // A git without sparse-index support for VFS expands a sparse index when it reads it, or
            // reports files under a collapsed folder as changed.
            string buildOptions = GitProcess.Invoke(this.Enlistment.RepoRoot, "version --build-options");
            if (!buildOptions.Contains("feature: vfs-sparse-index"))
            {
                Assert.Ignore("This git does not support a sparse index with VFS (git version --build-options has no 'feature: vfs-sparse-index')");
            }

            // The expected files come from the HEAD tree, not from the working directory, so that
            // the test does not create placeholders before the index collapses.
            Dictionary<string, string> headFiles = this.GitShouldSucceed("-c core.quotepath=false ls-tree -r HEAD")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split('\t'))
                .Where(parts => parts[0].Split(' ')[1] == "blob")
                .ToDictionary(parts => parts[1].Trim(), parts => parts[0].Split(' ')[2], StringComparer.OrdinalIgnoreCase);
            headFiles.Count.ShouldBeAtLeast(100, "HEAD has too few files");

            this.Enlistment.UnmountGVFS();

            // Turn on sparse-index support and the sparse index. The cone keeps only the files at the
            // root, so git collapses every top-level folder into a sparse-directory entry when it
            // writes the index. The persisted projection gets the same index, as it does when git
            // collapses the index while the repository is mounted.
            this.GitShouldSucceed("-c core.hookspath= config gvfs.auto-sparse-index true");
            this.GitShouldSucceed("-c core.hookspath= config core.sparseCheckout true");
            this.GitShouldSucceed("-c core.hookspath= config core.sparseCheckoutCone true");
            this.GitShouldSucceed("-c core.hookspath= config index.sparse true");
            File.WriteAllText(this.Enlistment.GetDotGitPath("info", "sparse-checkout"), "/*\n!/*/\n");
            this.GitShouldSucceed($"{BypassGVFS} -c sparse.expectFilesOutsideOfPatterns=true update-index --force-write-index");
            List<string> collapsedFolders = this.SparseDirectoryEntries();
            collapsedFolders.ShouldBeNonEmpty();
            File.Copy(this.Enlistment.GetDotGitPath("index"), Path.Combine(this.Enlistment.DotGVFSRoot, "GVFS_projection"), overwrite: true);

            this.Enlistment.MountGVFS();

            // Every file of HEAD is visible, and no other file is
            string workingDirectory = this.Enlistment.RepoRoot;
            string dotGit = Path.Combine(workingDirectory, ".git") + Path.DirectorySeparatorChar;
            List<string> projectedFiles = Directory.EnumerateFiles(workingDirectory, "*", SearchOption.AllDirectories)
                .Where(path => !path.StartsWith(dotGit, StringComparison.OrdinalIgnoreCase))
                .Select(path => path.Substring(workingDirectory.Length + 1).Replace(Path.DirectorySeparatorChar, '/'))
                .ToList();
            List<string> missing = headFiles.Keys.Except(projectedFiles, StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.Ordinal).ToList();
            List<string> extra = projectedFiles.Except(headFiles.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.Ordinal).ToList();
            missing.ShouldBeEmpty("Files of HEAD that the mount does not project: " + string.Join(", ", missing.Take(20)));
            extra.ShouldBeEmpty("Files that the mount projects, but HEAD does not have: " + string.Join(", ", extra.Take(20)));

            // Every regular file hydrates to the content of its blob in HEAD
            HashSet<string> regularFiles = new HashSet<string>(
                this.GitShouldSucceed("-c core.quotepath=false ls-tree -r HEAD")
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Where(line => line.StartsWith("100"))
                    .Select(line => line.Split('\t')[1].Trim()),
                StringComparer.OrdinalIgnoreCase);
            List<string> wrongContent = headFiles
                .Where(file => regularFiles.Contains(file.Key))
                .Where(file => BlobSha(File.ReadAllBytes(this.Enlistment.GetVirtualPathTo(file.Key))) != file.Value)
                .Select(file => file.Key)
                .ToList();
            wrongContent.ShouldBeEmpty("Files whose content is not the blob in HEAD: " + string.Join(", ", wrongContent.Take(20)));

            this.GitShouldSucceed("status --porcelain").ShouldEqual(string.Empty);

            // The index is still sparse, so the projection came from the sparse-directory entries
            this.SparseDirectoryEntries().ShouldMatchInOrder(collapsedFolders);
        }

        private string GitShouldSucceed(string command, string workingDirectory = null)
        {
            ProcessResult result = GitProcess.InvokeProcess(workingDirectory ?? this.Enlistment.RepoRoot, command);
            result.ExitCode.ShouldEqual(0, $"git {command} failed: {result.Errors}");
            return result.Output;
        }

        private static string BlobSha(byte[] content)
        {
            byte[] header = Encoding.ASCII.GetBytes($"blob {content.Length}\0");
            using (SHA1 sha1 = SHA1.Create())
            {
                sha1.TransformBlock(header, 0, header.Length, null, 0);
                sha1.TransformFinalBlock(content, 0, content.Length);
                return BitConverter.ToString(sha1.Hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private List<string> SparseDirectoryEntries()
        {
            // Without the virtual file system, git sees the projected folders outside the cone. By
            // default it then clears their skip-worktree bits and expands the index in memory.
            return this.GitShouldSucceed($"{BypassGVFS} -c sparse.expectFilesOutsideOfPatterns=true ls-files --sparse")
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.EndsWith("/"))
                .ToList();
        }
    }
}
