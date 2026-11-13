using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GVFS.Common.Sparse;
using GVFS.FunctionalTests.Properties;
using GVFS.Tests.Should;
using NUnit.Framework;

namespace GVFS.FunctionalTests.Tests.LocalComponents
{
    /// <summary>
    /// Compares <see cref="ConePathMatcher"/> with git's own cone matching. The test writes a
    /// cone from <see cref="ConeBuilder"/> to a temporary repository and asks
    /// <c>git sparse-checkout check-rules</c> about the same paths. No enlistment is needed.
    /// </summary>
    [TestFixture]
    public class ConePathMatcherGitTests
    {
        private const int GitTimeoutMilliseconds = 60000;

        // Modified paths for the comparison. They include directories that differ only in ASCII
        // case, directories that differ only in non-ASCII case, glob-special characters, and a
        // folder entry below a case variant.
        private static readonly string[] ModifiedPaths = new[]
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
        // ModifiedPaths.
        private static readonly string[] ExtraDirectories = new[]
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

        private string repoRoot;

        [SetUp]
        public void SetUp()
        {
            this.repoRoot = Path.Combine(Path.GetTempPath(), "ConePathMatcherGitTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(this.repoRoot, "repo"));
            this.RunGit(new[] { "init", "--quiet", "." }, input: null, out _, out _);
        }

        [TearDown]
        public void TearDown()
        {
            if (this.repoRoot != null && Directory.Exists(this.repoRoot))
            {
                Directory.Delete(this.repoRoot, recursive: true);
            }

            this.repoRoot = null;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MatcherAgreesWithGitCheckRules(bool ignoreCase)
        {
            ConePatternSet cone = ConeBuilder.BuildFromModifiedPaths(ModifiedPaths, ignoreCase);
            ConePathMatcher matcher = new ConePathMatcher(cone);
            List<string> queries = BuildQueries();

            string sparseCheckoutPath = Path.Combine(this.repoRoot, "repo", ".git", "info", "sparse-checkout");
            Directory.CreateDirectory(Path.GetDirectoryName(sparseCheckoutPath));
            File.WriteAllText(sparseCheckoutPath, ConeFileWriter.Serialize(cone), new UTF8Encoding(false));

            // check-rules reads the repository's own patterns here. With --rules-file and --cone
            // it would read directory names instead of raw patterns.
            string[] arguments = new[]
            {
                "-c", "core.ignoreCase=" + (ignoreCase ? "true" : "false"),
                "-c", "core.sparseCheckout=true",
                "-c", "core.sparseCheckoutCone=true",
                "sparse-checkout", "check-rules", "-z",
            };

            string input = string.Join("\0", queries) + "\0";
            this.RunGit(arguments, input, out string output, out string errors);

            // A warning means that git found a pattern it does not accept in cone mode, for
            // example a repeated directory, and turned off cone matching.
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
            List<string> modifiedOutsideCone = ModifiedPaths.Where(path => !gitInCone.Contains(path)).ToList();
            modifiedOutsideCone.ShouldBeEmpty(string.Join(", ", modifiedOutsideCone));

            // Make sure that the comparison covers both answers.
            gitInCone.Count.ShouldBeAtLeast(1);
            (queries.Count - gitInCone.Count).ShouldBeAtLeast(1);
        }

        private static List<string> BuildQueries()
        {
            HashSet<string> directories = new HashSet<string>(StringComparer.Ordinal);
            foreach (string path in ModifiedPaths.Concat(ExtraDirectories))
            {
                string current = path.TrimEnd('/');
                if (!ExtraDirectories.Contains(path) && !path.EndsWith("/", StringComparison.Ordinal))
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
            // upper and lower case: as a file inside it, a file in a child of it, a directory, and
            // a file with the directory's own name.
            HashSet<string> queries = new HashSet<string>(ModifiedPaths, StringComparer.Ordinal) { "ROOT.TXT" };
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

        private void RunGit(string[] arguments, string input, out string output, out string errors)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo(Settings.Default.PathToGit)
            {
                WorkingDirectory = Path.Combine(this.repoRoot, "repo"),
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

            // Configure only the child process, so that the functional runner's environment does
            // not change. Remove inherited repository and configuration overrides, and point
            // user and system configuration away from the machine's files.
            foreach (string key in startInfo.Environment.Keys.Where(key =>
                key.StartsWith("GIT_CONFIG", StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                startInfo.Environment.Remove(key);
            }

            startInfo.Environment.Remove("GIT_DIR");
            startInfo.Environment.Remove("GIT_COMMON_DIR");
            startInfo.Environment.Remove("GIT_WORK_TREE");
            startInfo.Environment.Remove("GIT_INDEX_FILE");
            startInfo.Environment.Remove("GIT_CEILING_DIRECTORIES");
            startInfo.Environment["HOME"] = this.repoRoot;
            startInfo.Environment["XDG_CONFIG_HOME"] = Path.Combine(this.repoRoot, "xdg");
            startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";

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
                process.ExitCode.ShouldEqual(0, "git " + string.Join(" ", arguments) + " failed: " + errors);
            }
        }
    }
}
