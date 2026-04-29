using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    [Category(Categories.GitCommands)]
    public class WindowsTombstoneTests : TestsWithEnlistmentPerFixture
    {
        private const string Delimiter = "\r\n";
        private const int TombstoneFolderPlaceholderType = 3;
        private const int MaxFileAccessRetries = 10;
        private const int FileAccessRetryDelayMs = 500;
        private FileSystemRunner fileSystem;

        public WindowsTombstoneTests()
        {
            this.fileSystem = new SystemIORunner();
        }

        [TestCase]
        public void CheckoutCleansUpTombstones()
        {
            const string folderToDelete = "Scripts";

            // Delete directory to create the tombstone
            string directoryToDelete = this.Enlistment.GetVirtualPathTo(folderToDelete);
            this.fileSystem.DeleteDirectory(directoryToDelete);

            DiagLog("Unmounting GVFS (first unmount)...");
            Stopwatch sw = Stopwatch.StartNew();
            this.Enlistment.UnmountGVFS();
            sw.Stop();
            DiagLog($"Unmount completed in {sw.ElapsedMilliseconds}ms");

            // Remove the directory entry from modified paths so git will not keep the folder up to date
            string modifiedPathsFile = Path.Combine(this.Enlistment.DotGVFSRoot, TestConstants.Databases.ModifiedPaths);

            DiagLog($"ModifiedPaths path: {modifiedPathsFile}");
            DiagLog($"ModifiedPaths exists: {File.Exists(modifiedPathsFile)}");
            if (File.Exists(modifiedPathsFile))
            {
                FileInfo fi = new FileInfo(modifiedPathsFile);
                DiagLog($"ModifiedPaths size: {fi.Length} bytes, lastWrite: {fi.LastWriteTimeUtc:O}");
            }

            string modifiedPathsContent = ReadFileWithRetry(modifiedPathsFile);
            DiagLog($"ModifiedPaths read OK, length: {modifiedPathsContent.Length} chars, lines: {modifiedPathsContent.Split(new[] { Delimiter }, StringSplitOptions.RemoveEmptyEntries).Length}");

            modifiedPathsContent = string.Join(Delimiter, modifiedPathsContent.Split(new[] { Delimiter }, StringSplitOptions.RemoveEmptyEntries).Where(x => !x.StartsWith($"A {folderToDelete}/")));
            string contentToWrite = modifiedPathsContent + Delimiter;
            DiagLog($"ModifiedPaths writing {contentToWrite.Length} chars...");
            WriteFileWithRetry(modifiedPathsFile, contentToWrite);
            DiagLog("ModifiedPaths write OK");

            // Verify file was written correctly
            string verifyContent = ReadFileWithRetry(modifiedPathsFile);
            DiagLog($"ModifiedPaths verify read: {verifyContent.Length} chars, match: {verifyContent == contentToWrite}");

            // Add tombstone folder entry to the placeholder database so the checkout will remove the tombstone
            // and start projecting the folder again
            string placeholderDatabasePath = Path.Combine(this.Enlistment.DotGVFSRoot, TestConstants.Databases.VFSForGit);
            DiagLog($"Placeholder DB path: {placeholderDatabasePath}, exists: {File.Exists(placeholderDatabasePath)}");
            GVFSHelpers.AddPlaceholderFolder(placeholderDatabasePath, folderToDelete, TombstoneFolderPlaceholderType);
            DiagLog("Placeholder folder entry added");

            DiagLog("Mounting GVFS (after ModifiedPaths edit)...");
            sw.Restart();

            string mountOutput;
            bool mountSucceeded = this.Enlistment.TryMountGVFS(out mountOutput);
            sw.Stop();
            DiagLog($"Mount returned in {sw.ElapsedMilliseconds}ms, success: {mountSucceeded}");
            if (!mountSucceeded)
            {
                // Dump diagnostics before failing
                DiagLog($"Mount output: {mountOutput}");
                DiagLog($"ModifiedPaths after failed mount exists: {File.Exists(modifiedPathsFile)}");
                if (File.Exists(modifiedPathsFile))
                {
                    try
                    {
                        string postMountContent = File.ReadAllText(modifiedPathsFile);
                        DiagLog($"ModifiedPaths content after failed mount ({postMountContent.Length} chars):");
                        DiagLog(postMountContent);
                    }
                    catch (Exception ex)
                    {
                        DiagLog($"Could not read ModifiedPaths after failed mount: {ex.GetType().Name}: {ex.Message}");
                    }
                }

                // Dump GVFS logs
                string gvfsLogsDir = Path.Combine(this.Enlistment.DotGVFSRoot, "logs");
                if (Directory.Exists(gvfsLogsDir))
                {
                    string[] logFiles = Directory.GetFiles(gvfsLogsDir, "*.log", SearchOption.TopDirectoryOnly);
                    DiagLog($"GVFS log files ({logFiles.Length}):");
                    foreach (string logFile in logFiles)
                    {
                        DiagLog($"  {Path.GetFileName(logFile)}");
                    }

                    // Dump tail of most recent mount log
                    string[] mountLogs = Directory.GetFiles(gvfsLogsDir, "mount_*", SearchOption.TopDirectoryOnly);
                    if (mountLogs.Length > 0)
                    {
                        string latestMountLog = mountLogs.OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc).First();
                        try
                        {
                            string[] mountLogLines = File.ReadAllLines(latestMountLog);
                            int tailCount = Math.Min(50, mountLogLines.Length);
                            DiagLog($"Last {tailCount} lines of {Path.GetFileName(latestMountLog)}:");
                            foreach (string line in mountLogLines.Skip(mountLogLines.Length - tailCount))
                            {
                                DiagLog($"  {line}");
                            }
                        }
                        catch (Exception ex)
                        {
                            DiagLog($"Could not read mount log: {ex.GetType().Name}: {ex.Message}");
                        }
                    }
                }

                Assert.Fail($"GVFS did not mount: {mountOutput}");
            }

            directoryToDelete.ShouldNotExistOnDisk(this.fileSystem);

            // checkout branch to remove tombstones and project the folder again
            GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, "checkout -f HEAD");
            directoryToDelete.ShouldBeADirectory(this.fileSystem);

            DiagLog("Unmounting GVFS (final unmount)...");
            this.Enlistment.UnmountGVFS();
            DiagLog("Final unmount completed");

            string placholders = GVFSHelpers.GetAllSQLitePlaceholdersAsString(placeholderDatabasePath);
            placholders.ShouldNotContain(ignoreCase: false, unexpectedSubstrings: $"{folderToDelete}{GVFSHelpers.PlaceholderFieldDelimiter}{TombstoneFolderPlaceholderType}{GVFSHelpers.PlaceholderFieldDelimiter}");
        }

        private static void DiagLog(string message)
        {
            Console.Error.WriteLine($"[TOMBSTONE-DIAG] {DateTime.UtcNow:O} {message}");
        }

        private static string ReadFileWithRetry(string path)
        {
            for (int attempt = 1; attempt <= MaxFileAccessRetries; attempt++)
            {
                try
                {
                    return File.ReadAllText(path);
                }
                catch (IOException ex) when (attempt < MaxFileAccessRetries)
                {
                    DiagLog($"ReadFile attempt {attempt}/{MaxFileAccessRetries} failed: {ex.GetType().Name}: {ex.Message}");
                    Thread.Sleep(FileAccessRetryDelayMs);
                }
            }

            // Final attempt — let it throw
            return File.ReadAllText(path);
        }

        private static void WriteFileWithRetry(string path, string content)
        {
            for (int attempt = 1; attempt <= MaxFileAccessRetries; attempt++)
            {
                try
                {
                    File.WriteAllText(path, content);
                    return;
                }
                catch (IOException ex) when (attempt < MaxFileAccessRetries)
                {
                    DiagLog($"WriteFile attempt {attempt}/{MaxFileAccessRetries} failed: {ex.GetType().Name}: {ex.Message}");
                    Thread.Sleep(FileAccessRetryDelayMs);
                }
            }

            // Final attempt — let it throw
            File.WriteAllText(path, content);
        }
    }
}
