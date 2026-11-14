using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    /// <summary>
    /// Functional tests for the cone requests that the hooks send when
    /// <c>gvfs.auto-sparse-index</c> is true. The mount does not handle these requests and
    /// answers <c>UnknownRequest</c>, so the tests check that Git still runs.
    /// </summary>
    [TestFixture]
    public class ConeManagementHookTests : TestsWithEnlistmentPerFixture
    {
        private const string AutoSparseIndexConfig = "gvfs.auto-sparse-index";

        // The parent-only directory GVFS puts the files directly in GVFS/ in the cone, but
        // not the files in GVFS/GVFS.Hooks/. GVFS does not set core.sparseCheckout, so Git
        // itself ignores this file.
        private const string ConeWithParentOnlyGVFS = "/*\n!/*/\n/GVFS/\n!/GVFS/*/\n";

        private const string InConeFile = "GVFS/cone-request-in-cone.txt";
        private const string OutOfConeFile = "GVFS/GVFS.Hooks/cone-request-out-of-cone.txt";

        private static readonly Dictionary<string, string> TraceEnvironment = new Dictionary<string, string>
        {
            { "GIT_TRACE", "1" },
        };

        [TestCase]
        public void GitRunsWhenTheMountDoesNotHandleConeRequests()
        {
            string sparseCheckoutPath = Path.Combine(this.Enlistment.RepoRoot, ".git", "info", "sparse-checkout");
            string originalSparseCheckout = File.Exists(sparseCheckoutPath) ? File.ReadAllText(sparseCheckoutPath) : null;

            try
            {
                File.WriteAllText(sparseCheckoutPath, ConeWithParentOnlyGVFS, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                GitProcess.Invoke(this.Enlistment.RepoRoot, "config --local " + AutoSparseIndexConfig + " true");
                File.WriteAllText(this.Enlistment.GetVirtualPathTo(InConeFile), "in cone");
                File.WriteAllText(this.Enlistment.GetVirtualPathTo(OutOfConeFile), "out of cone");

                // The test runs each command in a subdirectory, so the hook must resolve the
                // relative path through GIT_PREFIX to find the right cone answer.
                ProcessResult inCone = GitProcess.InvokeProcess(
                    Path.Combine(this.Enlistment.RepoRoot, "GVFS"),
                    "add " + Path.GetFileName(InConeFile),
                    TraceEnvironment);
                inCone.ExitCode.ShouldEqual(0, inCone.Errors);
                inCone.Errors.ShouldContain("gvfs cone-management: widen skipped: SkippedInCone");

                ProcessResult outOfCone = GitProcess.InvokeProcess(
                    Path.Combine(this.Enlistment.RepoRoot, "GVFS", "GVFS.Hooks"),
                    "add " + Path.GetFileName(OutOfConeFile),
                    TraceEnvironment);
                outOfCone.ExitCode.ShouldEqual(0, outOfCone.Errors);
                outOfCone.Errors.ShouldContain(
                    "gvfs cone-management: widen not applied: Rejected",
                    "gvfs cone-management: narrow not applied: Rejected");

                GitProcess.Invoke(this.Enlistment.RepoRoot, "diff --cached --name-only")
                    .ShouldContain(InConeFile, OutOfConeFile);
            }
            finally
            {
                GitProcess.Invoke(this.Enlistment.RepoRoot, "config --local --unset " + AutoSparseIndexConfig);
                GitProcess.Invoke(this.Enlistment.RepoRoot, "reset -q -- " + InConeFile + " " + OutOfConeFile);
                File.Delete(this.Enlistment.GetVirtualPathTo(InConeFile));
                File.Delete(this.Enlistment.GetVirtualPathTo(OutOfConeFile));

                if (originalSparseCheckout == null)
                {
                    File.Delete(sparseCheckoutPath);
                }
                else
                {
                    File.WriteAllText(sparseCheckoutPath, originalSparseCheckout);
                }
            }
        }
    }
}
