using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    /// <summary>
    /// Mounts the fixture's enlistment with <c>gvfs.auto-sparse-index</c> on, a root-only cone,
    /// and a collapsed sparse index, and has helpers for the tests of the sparse-index cone.
    /// </summary>
    public abstract class AutoSparseIndexTestsBase : TestsWithEnlistmentPerFixture
    {
        protected const string BypassGVFS = "-c core.virtualfilesystem= -c core.hookspath=";
        protected const string RootOnlyCone = "/*\n!/*/\n";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private int traceCount;

        /// <summary>The files of HEAD that are three or more levels deep and have no space in the path.</summary>
        protected List<string> DeepHeadFiles { get; private set; }

        protected string SparseCheckoutPath => this.Enlistment.GetDotGitPath("info", "sparse-checkout");

        [OneTimeSetUp]
        public void MountWithASparseIndex()
        {
            // A Git with this feature also changes only the index when the cone changes.
            string buildOptions = GitProcess.Invoke(this.Enlistment.RepoRoot, "version --build-options");
            if (!buildOptions.Contains("feature: vfs-sparse-index"))
            {
                Assert.Ignore("This git does not support a sparse index with VFS (git version --build-options has no 'feature: vfs-sparse-index')");
            }

            this.DeepHeadFiles = this.GitShouldSucceed("-c core.quotepath=false ls-tree -r --name-only HEAD")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(path => path.Trim())
                .Where(path => path.Count(character => character == '/') >= 2 && !path.Contains(' '))
                .ToList();

            // Mount with the feature on, a root-only cone, and a collapsed index. The persisted
            // projection gets the same index, as it does when git collapses the index while the
            // repository is mounted.
            this.Enlistment.UnmountGVFS();
            this.GitShouldSucceed("-c core.hookspath= config gvfs.auto-sparse-index true");
            this.GitShouldSucceed("-c core.hookspath= config core.sparseCheckout true");
            this.GitShouldSucceed("-c core.hookspath= config core.sparseCheckoutCone true");
            this.GitShouldSucceed("-c core.hookspath= config index.sparse true");
            File.WriteAllText(this.SparseCheckoutPath, RootOnlyCone, Utf8NoBom);
            this.GitShouldSucceed($"{BypassGVFS} -c sparse.expectFilesOutsideOfPatterns=true update-index --force-write-index");
            this.SparseDirectoryEntries().ShouldBeNonEmpty();
            File.Copy(this.Enlistment.GetDotGitPath("index"), Path.Combine(this.Enlistment.DotGVFSRoot, "GVFS_projection"), overwrite: true);
            this.Enlistment.MountGVFS();
        }

        protected static string[] ConeLines(string cone)
        {
            return cone.Split('\n');
        }

        protected static bool IsConeLineFor(string line, string directory)
        {
            return line.Equals("/" + directory + "/", StringComparison.OrdinalIgnoreCase);
        }

        protected static string TwoLevelDirectory(string path)
        {
            string[] segments = path.Split('/');
            return segments[0] + "/" + segments[1];
        }

        protected static string ParentDirectory(string path)
        {
            return path.Substring(0, path.LastIndexOf('/'));
        }

        protected static int CountFullIndexExpansions(string trace2EventFile)
        {
            File.Exists(trace2EventFile).ShouldBeTrue("git did not write the trace2 file " + trace2EventFile);
            return File.ReadLines(trace2EventFile)
                .Count(line => line.Contains("\"event\":\"region_enter\"") && line.Contains("\"label\":\"ensure_full_index\""));
        }

        /// <summary>
        /// Return the files of HEAD that are three or more levels deep, grouped by their
        /// two-level directory. Files of different groups are in different directories outside a
        /// root-only cone.
        /// </summary>
        protected List<IGrouping<string, string>> GroupByTwoLevelDirectory()
        {
            return this.DeepHeadFiles
                .GroupBy(path => TwoLevelDirectory(path), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        protected string[] WaitForConeFile(Func<string[], bool> isExpected)
        {
            string[] lines = ConeLines(this.ReadConeFile());
            for (int attempt = 0; attempt < 100 && !isExpected(lines); attempt++)
            {
                System.Threading.Thread.Sleep(100);
                lines = ConeLines(this.ReadConeFile());
            }

            return lines;
        }

        /// <summary>
        /// Read the cone file. The read lets the mount replace the file, as a read by Git does.
        /// </summary>
        protected string ReadConeFile()
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    using (FileStream stream = new FileStream(this.SparseCheckoutPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        return reader.ReadToEnd();
                    }
                }
                catch (IOException) when (attempt < 10)
                {
                    // The mount replaced the file between the lookup and the open.
                    System.Threading.Thread.Sleep(50);
                }
            }
        }

        protected ProcessResult GitWithTrace(string command, out string trace2EventFile, bool takeOptionalLocks = false)
        {
            string traceFile = Path.Combine(this.Enlistment.EnlistmentRoot, $"cone-trace2-{System.Threading.Interlocked.Increment(ref this.traceCount)}.json");
            trace2EventFile = traceFile;
            Dictionary<string, string> environment = new Dictionary<string, string>
            {
                { "GIT_TRACE", "1" },
                { "GIT_TRACE2_EVENT", traceFile },
            };

            // The test runner turns optional locks off, so "git status" does not take the GVFS
            // lock unless the test turns them on again.
            if (takeOptionalLocks)
            {
                environment.Add("GIT_OPTIONAL_LOCKS", "1");
            }

            return GitProcess.InvokeProcess(this.Enlistment.RepoRoot, command, environment);
        }

        protected string GitShouldSucceed(string command)
        {
            ProcessResult result = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, command);
            result.ExitCode.ShouldEqual(0, $"git {command} failed: {result.Errors}");
            return result.Output;
        }

        protected List<string> SparseDirectoryEntries()
        {
            return this.SparseIndexEntries().Where(line => line.EndsWith("/")).ToList();
        }

        protected List<string> SparseIndexEntries()
        {
            // Without the virtual file system, git sees the projected folders outside the cone. By
            // default it then clears their skip-worktree bits and expands the index in memory.
            return this.GitShouldSucceed($"{BypassGVFS} -c sparse.expectFilesOutsideOfPatterns=true -c core.quotepath=false ls-files --sparse")
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToList();
        }
    }
}
