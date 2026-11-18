using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using System.Threading;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    /// <summary>
    /// Functional tests for modified files outside the sparse-index cone that have the content of
    /// HEAD again: the mount returns them to placeholders, so that Git can collapse their
    /// directories, and a later write is still visible to Git.
    /// </summary>
    [TestFixture]
    public class AutoSparseIndexDehydrateTests : AutoSparseIndexTestsBase
    {
        // FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS: a placeholder whose content is not on disk.
        private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;

        private const string Edit = "edited after the dehydrate";

        private static readonly TimeSpan WaitTime = TimeSpan.FromSeconds(30);

        private readonly FileSystemRunner fileSystem = new SystemIORunner();

        // Files of HEAD, each in a different two-level directory outside the root-only cone
        private Queue<string> unusedFiles;

        [OneTimeSetUp]
        public void SelectFiles()
        {
            // One file in each directory, so that the tests do not share a cone line.
            this.unusedFiles = new Queue<string>(this.DeepHeadFiles
                .GroupBy(ParentDirectory, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First()));
            this.unusedFiles.Count.ShouldBeAtLeast(24, "HEAD has too few directories that are two or more levels deep");
        }

        [TestCase]
        public void DehydrateCollapsesTheDirectoryAndTheFileHasTheContentOfHead()
        {
            string file = this.TakeFile();
            byte[] headContent = this.MakeFileModifiedThenClean(file);

            // While the file is a modified path, the hook clears its skip-worktree bit, so Git
            // keeps its own index entry.
            this.SparseIndexEntries().ShouldContain(entry => entry.Equals(file, StringComparison.OrdinalIgnoreCase));

            this.RunGitAndWaitForTheDehydrate(file);

            List<string> entries = this.SparseIndexEntries();
            entries.ShouldNotContain(entry => entry.Equals(file, StringComparison.OrdinalIgnoreCase));
            entries.ShouldContain(entry => entry.EndsWith("/") && file.StartsWith(entry, StringComparison.OrdinalIgnoreCase));
            this.GitShouldSucceed("status --porcelain").ShouldNotContain(ignoreCase: true, file);
            File.ReadAllBytes(this.Enlistment.GetVirtualPathTo(file)).ShouldMatchInOrder(headContent);
            GVFSHelpers.ModifiedPathsShouldNotContain(this.Enlistment, this.fileSystem, file);
        }

        [TestCase]
        public void InPlaceWriteAfterTheDehydrateIsVisibleToGit()
        {
            string file = this.TakeDehydratedFile();

            using (FileStream stream = new FileStream(this.Enlistment.GetVirtualPathTo(file), FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            {
                stream.Seek(0, SeekOrigin.End);
                byte[] edit = Encoding.UTF8.GetBytes(Environment.NewLine + Edit + Environment.NewLine);
                stream.Write(edit, 0, edit.Length);
            }

            this.GitShouldSeeTheEdit(file, Edit);
        }

        [TestCase]
        public void MemoryMappedWriteAfterTheDehydrateIsVisibleToGit()
        {
            string file = this.TakeDehydratedFile();
            string path = this.Enlistment.GetVirtualPathTo(file);

            byte replaced;
            using (MemoryMappedFile mapping = MemoryMappedFile.CreateFromFile(path, FileMode.Open, mapName: null, capacity: 0, MemoryMappedFileAccess.ReadWrite))
            using (MemoryMappedViewAccessor view = mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite))
            {
                byte first = view.ReadByte(0);
                replaced = first == (byte)'#' ? (byte)'$' : (byte)'#';
                view.Write(0, replaced);
                view.Flush();
            }

            this.GitShouldSeeTheEdit(file, ((char)replaced).ToString());
        }

        [TestCase]
        public void TruncatingOverwriteAfterTheDehydrateIsVisibleToGit()
        {
            string file = this.TakeDehydratedFile();

            File.WriteAllText(this.Enlistment.GetVirtualPathTo(file), Edit + Environment.NewLine);

            this.GitShouldSeeTheEdit(file, Edit);
        }

        [TestCase]
        public void RenameOverSaveAfterTheDehydrateIsVisibleToGit()
        {
            string file = this.TakeDehydratedFile();
            string path = this.Enlistment.GetVirtualPathTo(file);
            string temporaryPath = path + ".save-tmp";

            File.WriteAllText(temporaryPath, Edit + Environment.NewLine);
            File.Move(temporaryPath, path, overwrite: true);

            this.GitShouldSeeTheEdit(file, Edit);
        }

        [TestCase]
        public void FileWithOtherBytesButTheSameSizeAndTimeIsNotDehydrated()
        {
            string file = this.TakeFile();
            string directory = ParentDirectory(file);
            string path = this.Enlistment.GetVirtualPathTo(file);
            byte[] headContent = File.ReadAllBytes(path);
            this.AppendTo(file);
            this.GitShouldSucceed("status --porcelain").ShouldContain(file);

            // Make the stat data of the index entry match the file, then change the bytes and
            // keep the size and the time. These steps take less time than the wait before the
            // mount checks the file.
            DateTimeOffset written = DateTimeOffset.Now;
            File.WriteAllBytes(path, headContent);

            // Other modified files can need an update, which makes the exit code 1.
            GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "update-index -q --refresh");
            DateTime lastWriteTime = File.GetLastWriteTimeUtc(path);
            byte[] otherContent = (byte[])headContent.Clone();
            otherContent[0] = otherContent[0] == (byte)'#' ? (byte)'$' : (byte)'#';
            File.WriteAllBytes(path, otherContent);
            File.SetLastWriteTimeUtc(path, lastWriteTime);

            // Git compares only the stat data, so the check of the mount finds the file clean.
            this.WaitForTheCheckToFindTheFileClean(directory, written);

            this.GitShouldSucceed("status --porcelain");

            File.ReadAllBytes(path).ShouldMatchInOrder(otherContent);
            this.IsPlaceholder(file).ShouldBeFalse();
            GVFSHelpers.ModifiedPathsShouldContain(this.Enlistment, this.fileSystem, file);
            File.WriteAllBytes(path, headContent);
        }

        [TestCase(FileAccess.Read, FileShare.Read)]
        [TestCase(FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete)]
        public void FileThatIsOpenIsNotDehydrated(FileAccess access, FileShare share)
        {
            string file = this.TakeFile();
            FileStream handle = null;
            try
            {
                // Open the file before the mount can check it.
                this.MakeFileModifiedThenClean(file, () => handle = new FileStream(this.Enlistment.GetVirtualPathTo(file), FileMode.Open, access, share));
                this.GitShouldSucceed("status --porcelain");

                this.IsPlaceholder(file).ShouldBeFalse();
                GVFSHelpers.ModifiedPathsShouldContain(this.Enlistment, this.fileSystem, file);
            }
            finally
            {
                handle?.Dispose();
            }
        }

        [TestCase]
        public void NoDehydrateDuringAMergeConflict()
        {
            string head = this.GitShouldSucceed("rev-parse HEAD").Trim();
            string ours = this.CommitWithoutTheWorkingTree(head, "ours");
            string theirs = this.CommitWithoutTheWorkingTree(head, "theirs");
            this.GitShouldSucceed("merge --ff-only " + ours);
            GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "merge --no-edit " + theirs).ExitCode.ShouldNotEqual(0, "The merge must stop with a conflict");
            File.Exists(this.Enlistment.GetDotGitPath("MERGE_HEAD")).ShouldBeTrue();

            string file = this.TakeFile();
            try
            {
                DateTimeOffset start = DateTimeOffset.Now;
                this.MakeFileModifiedThenClean(file);
                this.WaitForTheCheckToFindTheFileClean(ParentDirectory(file), start, requiredMetadata: "\"DehydrateBlocker\":\"MERGE_HEAD\"");
                this.GitShouldSucceed("status --porcelain");

                this.IsPlaceholder(file).ShouldBeFalse();
                GVFSHelpers.ModifiedPathsShouldContain(this.Enlistment, this.fileSystem, file);
            }
            finally
            {
                this.GitShouldSucceed("merge --abort");
                this.GitShouldSucceed("reset --hard " + head);
            }

            // The commands above change the index and HEAD, so the mount checks again, and the
            // next command dehydrates the file.
            this.RunGitAndWaitForTheDehydrate(file);
        }

        [TestCase("reset --hard", "append")]
        [TestCase("reset --hard", "replace")]
        [TestCase("reset --hard", "deletecreate")]
        [TestCase("checkout -f", "append")]
        [TestCase("restore --staged --worktree -- {0}", "append")]
        public void FileThatADiscardRestoredIsDehydratedAndItsDirectoryCollapses(string discardCommand, string editStyle)
        {
            string file = this.TakeFile();
            string directory = ParentDirectory(file);
            string path = this.Enlistment.GetVirtualPathTo(file);
            byte[] headContent = File.ReadAllBytes(path);
            switch (editStyle)
            {
                case "append":
                    this.AppendTo(file);
                    break;

                case "replace":
                    // The save pattern of many editors: write a new file, then rename it over the old one.
                    File.WriteAllText(path + ".save-tmp", "replaced" + Environment.NewLine);
                    File.Move(path + ".save-tmp", path, overwrite: true);
                    break;

                default:
                    File.Delete(path);
                    File.WriteAllText(path, "created again" + Environment.NewLine);
                    break;
            }

            this.GitShouldSucceed("add -- " + file);

            // The discard writes the content of HEAD and can rebuild the projection. The file
            // stays a modified path, and the check must still find it clean.
            DateTimeOffset discarded = DateTimeOffset.Now;
            this.GitShouldSucceed(string.Format(discardCommand, file));
            File.ReadAllBytes(path).ShouldMatchInOrder(headContent);
            this.WaitForTheCheckToFindTheFileClean(directory, discarded);

            this.RunGitAndWaitForTheDehydrate(file);

            this.SparseIndexEntries().ShouldContain(entry => entry.EndsWith("/") && file.StartsWith(entry, StringComparison.OrdinalIgnoreCase));
            this.GitShouldSucceed("status --porcelain").ShouldNotContain(ignoreCase: true, file);
            File.ReadAllBytes(path).ShouldMatchInOrder(headContent);

            // A folder dehydrate afterwards keeps the content of HEAD and a clean status.
            ProcessResult dehydrate = this.RunGVFS($"dehydrate --confirm --no-status --folders {directory}");
            dehydrate.ExitCode.ShouldEqual(0, dehydrate.Output + dehydrate.Errors);
            File.ReadAllBytes(path).ShouldMatchInOrder(headContent);
            this.GitShouldSucceed("status --porcelain").ShouldNotContain(ignoreCase: true, file);
            GVFSHelpers.ModifiedPathsShouldNotContain(this.Enlistment, this.fileSystem, file);
        }

        [TestCase]
        public void FolderDehydrateLeavesASparseIndexAndAConeWithTheOtherModifiedPaths()
        {
            string dirtyFile = this.TakeFile();
            this.AppendTo(dirtyFile);
            this.GitShouldSucceed("status --porcelain").ShouldContain(dirtyFile);

            string cleanFile = this.TakeFile();
            byte[] headContent = this.MakeFileModifiedThenClean(cleanFile);
            string folder = ParentDirectory(cleanFile);

            ProcessResult dehydrate = this.RunGVFS($"dehydrate --confirm --no-status --folders {folder}");
            dehydrate.ExitCode.ShouldEqual(0, dehydrate.Output + dehydrate.Errors);

            this.IsPlaceholder(cleanFile).ShouldBeTrue();
            GVFSHelpers.ModifiedPathsShouldNotContain(this.Enlistment, this.fileSystem, cleanFile);
            GVFSHelpers.ModifiedPathsShouldContain(this.Enlistment, this.fileSystem, dirtyFile);
            this.GitShouldSucceed("status --porcelain").ShouldContain(" M " + dirtyFile);
            this.ReadConeLines().ShouldContain(line => IsConeLineFor(line, ParentDirectory(dirtyFile)));
            List<string> entries = this.SparseIndexEntries();
            entries.ShouldContain(entry => entry.EndsWith("/") && cleanFile.StartsWith(entry, StringComparison.OrdinalIgnoreCase));
            File.ReadAllBytes(this.Enlistment.GetVirtualPathTo(cleanFile)).ShouldMatchInOrder(headContent);

            this.GitShouldSucceed("checkout -- " + dirtyFile);
        }

        [TestCase]
        public void FileWithAMappedViewIsNotDehydratedAndAWriteThroughTheViewIsVisibleToGit()
        {
            string file = this.TakeFile();
            string path = this.Enlistment.GetVirtualPathTo(file);

            // Map the file before the mount can check it. After the handle closes, only the
            // mapped view refers to the file, so the share mode of the check does not see it.
            MemoryMappedFile mapping = null;
            byte[] headContent = this.MakeFileModifiedThenClean(file, () =>
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
                {
                    mapping = MemoryMappedFile.CreateFromFile(stream, mapName: null, capacity: 0, MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, leaveOpen: true);
                }
            });

            byte replaced;
            using (mapping)
            using (MemoryMappedViewAccessor view = mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite))
            {
                this.GitShouldSucceed("status --porcelain");

                this.IsPlaceholder(file).ShouldBeFalse();
                GVFSHelpers.ModifiedPathsShouldContain(this.Enlistment, this.fileSystem, file);

                byte first = view.ReadByte(0);
                replaced = first == (byte)'#' ? (byte)'$' : (byte)'#';
                view.Write(0, replaced);
                view.Flush();
            }

            // A write through a view whose handle is closed does not change the time of the file,
            // so Git's stat check can miss it. The write is on disk, and the file is still a
            // modified path, so Git sees it once the stat data changes.
            File.ReadAllBytes(path)[0].ShouldEqual(replaced);
            this.IsPlaceholder(file).ShouldBeFalse();
            GVFSHelpers.ModifiedPathsShouldContain(this.Enlistment, this.fileSystem, file);
            File.WriteAllBytes(path, headContent);
        }

        private string TakeFile()
        {
            while (true)
            {
                // The length of a placeholder does not hydrate it.
                string file = this.unusedFiles.Dequeue();
                if (new FileInfo(this.Enlistment.GetVirtualPathTo(file)).Length > 0)
                {
                    return file;
                }
            }
        }

        private ProcessResult RunGVFS(string arguments)
        {
            ProcessStartInfo processInfo = new ProcessStartInfo(GVFSTestConfig.PathToGVFS);
            processInfo.Arguments = arguments + " " + TestConstants.InternalUseOnlyFlag + " " + GVFSHelpers.GetInternalParameter();
            processInfo.WindowStyle = ProcessWindowStyle.Hidden;
            processInfo.WorkingDirectory = this.Enlistment.EnlistmentRoot;
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardOutput = true;
            processInfo.RedirectStandardError = true;
            return ProcessHelper.Run(processInfo);
        }

        private string TakeDehydratedFile()
        {
            string file = this.TakeFile();
            this.MakeFileModifiedThenClean(file);
            this.RunGitAndWaitForTheDehydrate(file);
            return file;
        }

        /// <summary>
        /// Edit a file outside the cone, let Git see the edit, and write the content of HEAD back.
        /// Return when the check of the mount found the file clean. Before the check,
        /// <paramref name="afterHeadContentIsWritten"/> runs.
        /// </summary>
        private byte[] MakeFileModifiedThenClean(string file, Action afterHeadContentIsWritten = null)
        {
            string directory = ParentDirectory(file);
            string path = this.Enlistment.GetVirtualPathTo(file);
            byte[] headContent = File.ReadAllBytes(path);
            headContent.Length.ShouldBeAtLeast(1, file + " is empty");

            this.AppendTo(file);
            this.GitShouldSucceed("status --porcelain").ShouldContain(file);
            this.ReadConeLines().ShouldContain(line => IsConeLineFor(line, directory));

            DateTimeOffset written = DateTimeOffset.Now;
            File.WriteAllBytes(path, headContent);
            afterHeadContentIsWritten?.Invoke();
            this.WaitForTheCheckToFindTheFileClean(directory, written);

            // A Git command that runs in the background (for example the status cache of the
            // mount) can dehydrate the file from here on.
            return headContent;
        }

        /// <summary>
        /// The check made an apply pending. Run a Git command, which sends the apply, and wait
        /// until the file is a placeholder and not a modified path.
        /// </summary>
        private void RunGitAndWaitForTheDehydrate(string file)
        {
            this.GitShouldSucceed("status --porcelain");

            DateTime end = DateTime.UtcNow + WaitTime;
            while (true)
            {
                bool isModifiedPath = GVFSHelpers.GetCurrentModifiedPaths(this.Enlistment, this.fileSystem)
                    .Contains(file);
                bool isPlaceholder = this.IsPlaceholder(file);
                if (!isModifiedPath && isPlaceholder)
                {
                    return;
                }

                if (DateTime.UtcNow > end)
                {
                    Assert.Fail($"{file} was not dehydrated: modified path {isModifiedPath}, placeholder {isPlaceholder}. Last cone events:\n{this.LastConeEvents()}");
                }

                Thread.Sleep(250);
                this.GitShouldSucceed("status --porcelain");
            }
        }

        /// <summary>
        /// Wait until the cone no longer includes <paramref name="directory"/>, or until the
        /// mount log has a check after <paramref name="since"/> that found a clean file. A
        /// narrow can fail while another Git process reads the cone file; the next apply then
        /// writes it.
        /// </summary>
        private void WaitForTheCheckToFindTheFileClean(string directory, DateTimeOffset since, string requiredMetadata = null)
        {
            DateTime end = DateTime.UtcNow + WaitTime;
            while (DateTime.UtcNow < end)
            {
                bool narrowed = requiredMetadata == null && !this.ReadConeLines().Any(line => IsConeLineFor(line, directory));
                if (narrowed || this.ConeEvents(since).Any(line => line.Contains("] ConeReconcile ") && !line.Contains("\"NewlyCleanCount\":0") && line.Contains("NewlyCleanCount") && (requiredMetadata == null || line.Contains(requiredMetadata))))
                {
                    return;
                }

                Thread.Sleep(100);
            }

            Assert.Fail($"The mount did not find the file in {directory} clean. Last cone events:\n{this.LastConeEvents()}");
        }

        private List<string> ConeEvents(DateTimeOffset since)
        {
            List<string> events = new List<string>();
            foreach (string line in this.ConeEventLines())
            {
                // "[2026-11-17 14:25:56.6848 -08:00] ConeReconcile {...}"
                int close = line.IndexOf(']');
                if (line.StartsWith("[") && close > 0 &&
                    DateTimeOffset.TryParse(line.Substring(1, close - 1), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTimeOffset time) &&
                    time >= since)
                {
                    events.Add(line);
                }
            }

            return events;
        }

        private string LastConeEvents()
        {
            List<string> lines = this.ConeEventLines();
            return string.Join("\n", lines.Skip(Math.Max(0, lines.Count - 12)));
        }

        private List<string> ConeEventLines()
        {
            string logs = Path.Combine(this.Enlistment.DotGVFSRoot, "logs");
            string log = Directory.Exists(logs)
                ? Directory.GetFiles(logs, "*mount_process*").OrderBy(File.GetLastWriteTimeUtc).LastOrDefault()
                : null;
            List<string> lines = new List<string>();
            if (log == null)
            {
                return lines;
            }

            using (StreamReader reader = new StreamReader(new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Contains("] Cone") || line.Contains("TryDehydrateCleanFile"))
                    {
                        lines.Add(line);
                    }
                }

                return lines;
            }
        }

        private void GitShouldSeeTheEdit(string file, string editText)
        {
            this.GitShouldSucceed("status --porcelain").ShouldContain(" M " + file);
            this.GitShouldSucceed("diff -- " + file).ShouldContain(editText);
            GVFSHelpers.ModifiedPathsShouldContain(this.Enlistment, this.fileSystem, file);
            this.GitShouldSucceed("checkout -- " + file);
        }

        private bool IsPlaceholder(string file)
        {
            return (File.GetAttributes(this.Enlistment.GetVirtualPathTo(file)) & RecallOnDataAccess) != 0;
        }

        private void AppendTo(string file)
        {
            File.AppendAllText(this.Enlistment.GetVirtualPathTo(file), Environment.NewLine + "edited outside the cone" + Environment.NewLine);
        }

        /// <summary>
        /// Commit a new root file with <paramref name="content"/> on top of
        /// <paramref name="parent"/> with a separate index and without the virtual file system,
        /// so that the working tree, the index, and the cone do not change.
        /// </summary>
        private string CommitWithoutTheWorkingTree(string parent, string content)
        {
            string indexFile = Path.Combine(this.Enlistment.EnlistmentRoot, "commit-" + Guid.NewGuid().ToString("N") + ".index");
            string plainGit = BypassGVFS + " -c core.sparseCheckout=false -c index.sparse=false";
            Dictionary<string, string> environment = new Dictionary<string, string> { { "GIT_INDEX_FILE", indexFile } };
            try
            {
                this.PlainGitShouldSucceed($"{plainGit} read-tree {parent}", environment);
                string blob;
                using (MemoryStream input = new MemoryStream(Encoding.UTF8.GetBytes(content + "\n")))
                {
                    blob = this.PlainGitShouldSucceed("hash-object -w --no-filters --stdin", environment, input).Trim();
                }

                this.PlainGitShouldSucceed($"{plainGit} update-index --add --cacheinfo 100644,{blob},dehydrate-merge-conflict.txt", environment);
                string tree = this.PlainGitShouldSucceed($"{plainGit} write-tree", environment).Trim();
                return this.PlainGitShouldSucceed($"{plainGit} commit-tree {tree} -p {parent} -m \"Commit beside the working tree\"", environment).Trim();
            }
            finally
            {
                File.Delete(indexFile);
            }
        }

        private string PlainGitShouldSucceed(string command, Dictionary<string, string> environment, Stream input = null)
        {
            ProcessResult result = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, command, environment, input);
            result.ExitCode.ShouldEqual(0, $"git {command} failed: {result.Errors}");
            return result.Output;
        }
    }
}
