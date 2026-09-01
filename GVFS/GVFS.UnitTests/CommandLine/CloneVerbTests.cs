using GVFS.Common;
using GVFS.CommandLine;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System;
using System.IO;

namespace GVFS.UnitTests.CommandLine
{
    [TestFixture]
    public class CloneVerbTests
    {
        private CloneVerb cloneVerb;
        private string testDir;

        [SetUp]
        public void Setup()
        {
            this.cloneVerb = new CloneVerb();
            this.testDir = Path.Combine(Path.GetTempPath(), "CloneVerbTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(this.testDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(this.testDir))
            {
                Directory.Delete(this.testDir, recursive: true);
            }
        }

        [TestCase]
        public void TryCreateEnlistmentFailsWithoutEnlistmentWhenTargetDirectoryIsNotEmpty()
        {
            File.WriteAllText(Path.Combine(this.testDir, "preexisting.txt"), "content");

            CloneVerb.Result result = this.cloneVerb.TryCreateEnlistment(
                this.testDir,
                this.testDir,
                out GVFSEnlistment enlistment);

            Assert.IsFalse(result.Success);
            Assert.IsNull(enlistment);
            StringAssert.Contains("exists and is not empty", result.ErrorMessage);
        }

        [TestCase]
        public void TryCreateEnlistmentDoesNotFailForEmptyTargetDirectory()
        {
            // testDir is created empty by Setup and never written to in this test.
            CloneVerb.Result result = this.cloneVerb.TryCreateEnlistment(
                this.testDir,
                this.testDir,
                out GVFSEnlistment enlistment);

            StringAssert.DoesNotContain("exists and is not empty", result.ErrorMessage ?? string.Empty);
        }

        [TestCase]
        public void TryCreateEnlistmentReportsNormalizedPathWhenItDiffersFromFullPath()
        {
            File.WriteAllText(Path.Combine(this.testDir, "preexisting.txt"), "content");
            string fullPath = this.testDir + Path.DirectorySeparatorChar;

            CloneVerb.Result result = this.cloneVerb.TryCreateEnlistment(
                fullPath,
                this.testDir,
                out GVFSEnlistment enlistment);

            Assert.IsFalse(result.Success);
            Assert.IsNull(enlistment);
            StringAssert.Contains($"'{fullPath}'", result.ErrorMessage);
            StringAssert.Contains($"['{this.testDir}']", result.ErrorMessage);
        }

        // Regression test: this is the actual code path that used to throw a
        // NullReferenceException when `gvfs clone` targeted a non-empty directory.
        // TryCreateEnlistment (above) fails and returns a null enlistment; CloneVerb.Execute()
        // used to unconditionally dereference that null enlistment to read the trustPackIndexes
        // config, crashing instead of reporting the "exists and is not empty" error. Execute()
        // itself cannot be unit-tested (it terminates the process via Environment.Exit()), so
        // GetTrustPackIndexes was extracted as the smallest testable seam that reproduces the
        // exact failure condition: a failed clone result with a null enlistment.
        [TestCase]
        public void GetTrustPackIndexesDoesNotThrowWhenCloneFailedAndEnlistmentIsNull()
        {
            MockTracer tracer = new MockTracer();
            CloneVerb.Result failedCloneResult = new CloneVerb.Result("Clone directory exists and is not empty");

            bool trustPackIndexes = true;
            Assert.DoesNotThrow(() => trustPackIndexes = this.cloneVerb.GetTrustPackIndexes(tracer, failedCloneResult, enlistment: null));

            Assert.AreEqual(GVFSConstants.GitConfig.TrustPackIndexesDefault, trustPackIndexes);
        }
    }
}
