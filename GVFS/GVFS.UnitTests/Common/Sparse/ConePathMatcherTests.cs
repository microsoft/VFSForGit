using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GVFS.Common.Sparse;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using NUnit.Framework;

namespace GVFS.UnitTests.Common.Sparse
{
    [TestFixture]
    public class ConePathMatcherTests
    {
        private const int GitTimeoutMilliseconds = 60000;

        // Modified paths for the comparison with git. They include directories that differ
        // only in ASCII case, directories that differ only in non-ASCII case, glob-special
        // characters, and a folder entry below a case variant.
        private static readonly string[] ComparisonModifiedPaths = new[]
        {
            "root.txt",
            "A/B/b.txt",
            "A/D/",
            "src/x.cs",
            "Src/y.cs",
            "SRC/Deep/z.cs",
            "lower/UPPER/f.txt",
            "Lower/upper/Sub/",
            "\u00C4/x.txt",
            "\u00E4/y.txt",
            "glob*dir/f.txt",
            "q?dir/sub/f.txt",
            "br[1]/f.txt",
        };

        // Directories that the comparison queries, in addition to every directory from
        // ComparisonModifiedPaths.
        private static readonly string[] ComparisonExtraDirectories = new[]
        {
            "A",
            "A/C",
            "A/B/E",
            "A/D/E/F",
            "X",
            "src/other",
            "SRC/Deep/er",
            "Lower/upper/Sub/x",
            "lower/other",
            "\u00C4/sub",
            "globXdir",
            "brX1]",
        };

        // Git variables that would point the test's git commands at another repository.
        private static readonly string[] RepositoryEnvironmentVariables = new[]
        {
            "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_COMMON_DIR", "GIT_CEILING_DIRECTORIES", "GIT_CONFIG_PARAMETERS", "GIT_CONFIG_COUNT",
        };

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void ConstructorThrowsOnNullCone()
        {
            Assert.Throws<ArgumentNullException>(() => new ConePathMatcher(null));
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void IsInConeThrowsOnNullPath()
        {
            ConePathMatcher matcher = new ConePathMatcher(Cone());
            Assert.Throws<ArgumentNullException>(() => matcher.IsInCone(null));
        }

        [TestCase("root.txt", true)]
        [TestCase("", true)]
        [TestCase("/", true)]
        [TestCase("X/f.txt", false)]
        [TestCase("X/", false)]
        [TestCase("A/f.txt", true)]
        [TestCase("A/", true)]
        [TestCase("A/C/f.txt", false)]
        [TestCase("A/C/", false)]
        [TestCase("A/B/f.txt", true)]
        [TestCase("A/B/", true)]
        [TestCase("A/B/E/f.txt", false)]
        [TestCase("A/D/f.txt", true)]
        [TestCase("A/D/E/F/f.txt", true)]
        [TestCase("A/D/E/F/", true)]
        [TestCase(@"A\D\E\f.txt", true)]
        [TestCase("/A/f.txt", true)]
        public void IsInConeFollowsConeRules(string path, bool expected)
        {
            // Parent-only A and A/B, recursive A/D.
            ConePathMatcher matcher = new ConePathMatcher(
                ConeBuilder.BuildFromModifiedPaths(new[] { "A/B/b.txt", "A/D/" }, ignoreCase: false));

            matcher.IsInCone(path).ShouldEqual(expected);
        }

        [TestCase]
        public void PathThatIsItselfRecursiveIsInCone()
        {
            // Git checks the full path against the recursive set before it checks the parent,
            // so the path is in the cone even when the cone omits the parent pair. For a
            // directory query, git checks the file "-" inside the directory the same way.
            ConePathMatcher matcher = new ConePathMatcher(Cone(recursive: new[] { "A/B", "C/-" }));

            matcher.IsInCone("A/B").ShouldBeTrue();
            matcher.IsInCone("A/B/f.txt").ShouldBeTrue();
            matcher.IsInCone("A/f.txt").ShouldBeFalse();
            matcher.IsInCone("C/").ShouldBeTrue();
            matcher.IsInCone("C/f.txt").ShouldBeFalse();
        }

        [TestCase(false, "a/f.txt", false)]
        [TestCase(true, "a/f.txt", true)]
        [TestCase(true, "a/c/f.txt", false)]
        [TestCase(true, "a/d/e/f.txt", true)]
        [TestCase(true, "a/D/", true)]
        [TestCase(false, "\u00C4/f.txt", true)]
        [TestCase(false, "\u00E4/f.txt", false)]
        [TestCase(true, "\u00E4/f.txt", false)]
        public void IgnoreCaseFoldsAsciiOnly(bool ignoreCase, string path, bool expected)
        {
            // The matcher uses the case rule of the cone. Git folds only ASCII letters under
            // core.ignoreCase (see GitPathComparer).
            ConePathMatcher matcher = new ConePathMatcher(
                Cone(parentOnly: new[] { "A", "\u00C4" }, recursive: new[] { "A/D" }, ignoreCase: ignoreCase));

            matcher.IsInCone(path).ShouldEqual(expected);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MatcherAgreesWithGitCheckRules(bool ignoreCase)
        {
            string gitPath = GitTestHelper.FindGitOnPath();
            if (gitPath == null)
            {
                Assert.Ignore("Git was not found on PATH.");
            }

            ConePatternSet cone = ConeBuilder.BuildFromModifiedPaths(ComparisonModifiedPaths, ignoreCase);
            ConePathMatcher matcher = new ConePathMatcher(cone);
            List<string> queries = BuildComparisonQueries();

            string repoRoot = Path.Combine(Path.GetTempPath(), "ConePathMatcherTests_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(repoRoot);
                RunGit(gitPath, repoRoot, new[] { "init", "--quiet", "." }, input: null, out _, out _);

                // "sparse-checkout check-rules" is newer than the oldest git that GVFS supports.
                RunGit(gitPath, repoRoot, new[] { "sparse-checkout", "-h" }, input: null, out string usage, out string usageErrors, allowFailure: true);
                if (!(usage + usageErrors).Contains("check-rules", StringComparison.Ordinal))
                {
                    Assert.Ignore("This git does not have 'git sparse-checkout check-rules'.");
                }

                string sparseCheckoutPath = Path.Combine(repoRoot, ".git", "info", "sparse-checkout");
                Directory.CreateDirectory(Path.GetDirectoryName(sparseCheckoutPath));
                File.WriteAllText(sparseCheckoutPath, ConeFileWriter.Serialize(cone), new UTF8Encoding(false));

                // check-rules reads the repository's own patterns here. With --rules-file and
                // --cone it would read directory names instead of raw patterns.
                string[] arguments = new[]
                {
                    "-c", "core.ignoreCase=" + (ignoreCase ? "true" : "false"),
                    "-c", "core.sparseCheckout=true",
                    "-c", "core.sparseCheckoutCone=true",
                    "sparse-checkout", "check-rules", "-z",
                };

                string input = string.Join("\0", queries) + "\0";
                RunGit(gitPath, repoRoot, arguments, input, out string output, out string errors);

                // A warning means that git found a pattern it does not accept in cone mode,
                // for example a repeated directory, and turned off cone matching.
                errors.ShouldEqual(string.Empty, "git reported: " + errors);

                HashSet<string> gitInCone = new HashSet<string>(
                    output.Split('\0', StringSplitOptions.RemoveEmptyEntries),
                    StringComparer.Ordinal);

                List<string> disagreements = queries
                    .Where(query => matcher.IsInCone(query) != gitInCone.Contains(query))
                    .Select(query => query + " (git: " + gitInCone.Contains(query) + ")")
                    .ToList();

                disagreements.ShouldBeEmpty(string.Join(", ", disagreements));

                // Every modified path must be in the cone, as git sees it.
                List<string> modifiedOutsideCone = ComparisonModifiedPaths.Where(path => !gitInCone.Contains(path)).ToList();
                modifiedOutsideCone.ShouldBeEmpty(string.Join(", ", modifiedOutsideCone));

                // Make sure that the comparison covers both answers.
                gitInCone.Count.ShouldBeAtLeast(1);
                (queries.Count - gitInCone.Count).ShouldBeAtLeast(1);
            }
            finally
            {
                if (Directory.Exists(repoRoot))
                {
                    Directory.Delete(repoRoot, recursive: true);
                }
            }
        }

        private static List<string> BuildComparisonQueries()
        {
            HashSet<string> directories = new HashSet<string>(StringComparer.Ordinal);
            foreach (string path in ComparisonModifiedPaths.Concat(ComparisonExtraDirectories))
            {
                string current = path.TrimEnd('/');
                if (!ComparisonExtraDirectories.Contains(path) && !path.EndsWith("/", StringComparison.Ordinal))
                {
                    int lastSlash = current.LastIndexOf('/');
                    current = lastSlash < 0 ? string.Empty : current.Substring(0, lastSlash);
                }

                while (current.Length != 0)
                {
                    directories.Add(current);
                    int lastSlash = current.LastIndexOf('/');
                    current = lastSlash < 0 ? string.Empty : current.Substring(0, lastSlash);
                }
            }

            // Query the modified paths themselves, then each directory in its own case and in
            // upper and lower case: as a file inside it, a file in a child of it, a directory,
            // and a file with the directory's own name.
            HashSet<string> queries = new HashSet<string>(ComparisonModifiedPaths, StringComparer.Ordinal) { "ROOT.TXT" };
            foreach (string directory in directories)
            {
                foreach (string spelling in new[] { directory, directory.ToUpperInvariant(), directory.ToLowerInvariant() })
                {
                    queries.Add(spelling + "/f.txt");
                    queries.Add(spelling + "/child/f.txt");
                    queries.Add(spelling + "/");
                    queries.Add(spelling);
                }
            }

            List<string> sorted = queries.ToList();
            sorted.Sort(StringComparer.Ordinal);
            return sorted;
        }

        private static void RunGit(
            string gitPath,
            string workingDirectory,
            string[] arguments,
            string input,
            out string output,
            out string errors,
            bool allowFailure = false)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo(gitPath)
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
            };

            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            // Keep machine and user configuration, and any repository of the caller, out of
            // the result.
            foreach (string variable in RepositoryEnvironmentVariables)
            {
                startInfo.Environment.Remove(variable);
            }

            startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            startInfo.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(workingDirectory, "empty-global-config");

            using (Process process = Process.Start(startInfo))
            {
                // Read both outputs before writing the input, so that a full output pipe cannot
                // block git while the test writes.
                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();

                if (input != null)
                {
                    process.StandardInput.Write(input);
                }

                process.StandardInput.Close();

                if (!process.WaitForExit(GitTimeoutMilliseconds))
                {
                    process.Kill(entireProcessTree: true);
                    Assert.Fail("git " + string.Join(" ", arguments) + " did not finish in time.");
                }

                output = outputTask.Result;
                errors = errorTask.Result;

                if (!allowFailure)
                {
                    process.ExitCode.ShouldEqual(0, "git " + string.Join(" ", arguments) + " failed: " + errors);
                }
            }
        }

        private static ConePatternSet Cone(string[] parentOnly = null, string[] recursive = null, bool ignoreCase = false)
        {
            return new ConePatternSet(parentOnly ?? new string[0], recursive ?? new string[0], ignoreCase);
        }
    }
}
