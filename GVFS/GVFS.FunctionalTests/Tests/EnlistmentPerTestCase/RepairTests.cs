using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerTestCase
{
    [TestFixture]
    [Category(Categories.ExtraCoverage)]
    public class RepairTests : TestsWithEnlistmentPerTestCase
    {
        [OneTimeSetUp]
        public void TurnOfflineIOOn()
        {
            GVFSHelpers.RegisterForOfflineIO();
        }

        [OneTimeTearDown]
        public void TurnOfflineIOOff()
        {
            GVFSHelpers.UnregisterForOfflineIO();
        }

        [TestCase]
        public void NoFixesNeeded()
        {
            this.Enlistment.UnmountGVFS();
            this.Enlistment.Repair(confirm: false);
            this.Enlistment.Repair(confirm: true);
        }

        [TestCase]
        public void FixesCorruptHeadSha()
        {
            this.Enlistment.UnmountGVFS();

            string headFilePath = Path.Combine(this.Enlistment.RepoBackingRoot, ".git", "HEAD");
            File.WriteAllText(headFilePath, "0000");
            this.Enlistment.TryMountGVFS().ShouldEqual(false, "GVFS shouldn't mount when HEAD is corrupt");

            this.RepairWithoutConfirmShouldNotFix();

            this.RepairWithConfirmShouldFix();
        }

        [TestCase]
        public void FixesCorruptHeadSymRef()
        {
            this.Enlistment.UnmountGVFS();

            string headFilePath = Path.Combine(this.Enlistment.RepoBackingRoot, ".git", "HEAD");
            File.WriteAllText(headFilePath, "ref: refs");
            this.Enlistment.TryMountGVFS().ShouldEqual(false, "GVFS shouldn't mount when HEAD is corrupt");

            this.RepairWithoutConfirmShouldNotFix();

            this.RepairWithConfirmShouldFix();
        }

        [TestCase]
        public void FixesMissingGitIndex()
        {
            this.Enlistment.UnmountGVFS();

            string gitIndexPath = Path.Combine(this.Enlistment.RepoBackingRoot, ".git", "index");
            File.Delete(gitIndexPath);
            this.Enlistment.TryMountGVFS().ShouldEqual(false, "GVFS shouldn't mount when git index is missing");

            this.RepairWithoutConfirmShouldNotFix();

            this.RepairWithConfirmShouldFix();
        }

        [TestCase]
        public void FixesGitIndexCorruptedWithBadData()
        {
            this.Enlistment.UnmountGVFS();

            string gitIndexPath = Path.Combine(this.Enlistment.RepoBackingRoot, ".git", "index");
            this.CreateCorruptIndexAndRename(
                gitIndexPath,
                (current, temp) =>
                {
                    byte[] badData = Encoding.ASCII.GetBytes("BAD_INDEX");
                    temp.Write(badData, 0, badData.Length);
                });

            string output;
            this.Enlistment.TryMountGVFS(out output).ShouldEqual(false, "GVFS shouldn't mount when index is corrupt");
            output.ShouldContain("Index validation failed");

            this.RepairWithoutConfirmShouldNotFix();

            this.RepairWithConfirmShouldFix();
        }

        [TestCase]
        public void FixesGitIndexContainingAllNulls()
        {
            this.Enlistment.UnmountGVFS();

            string gitIndexPath = Path.Combine(this.Enlistment.RepoBackingRoot, ".git", "index");

            // Set the contents of the index file to gitIndexPath NULL
            this.CreateCorruptIndexAndRename(
                gitIndexPath,
                (current, temp) =>
                {
                    temp.Write(Enumerable.Repeat<byte>(0, (int)current.Length).ToArray(), 0, (int)current.Length);
                });

            string output;
            this.Enlistment.TryMountGVFS(out output).ShouldEqual(false, "GVFS shouldn't mount when index is corrupt");
            output.ShouldContain("Index validation failed");

            this.RepairWithoutConfirmShouldNotFix();

            this.RepairWithConfirmShouldFix();
        }

        [TestCase]
        public void FixesGitIndexCorruptedByTruncation()
        {
            this.Enlistment.UnmountGVFS();

            string gitIndexPath = Path.Combine(this.Enlistment.RepoBackingRoot, ".git", "index");

            // Truncate the contents of the index
            this.CreateCorruptIndexAndRename(
                gitIndexPath,
                (current, temp) =>
                {
                    // 20 will truncate the file in the middle of the first entry in the index
                    byte[] currentStartOfIndex = new byte[20];
                    current.Read(currentStartOfIndex, 0, currentStartOfIndex.Length);
                    temp.Write(currentStartOfIndex, 0, currentStartOfIndex.Length);
                });

            string output;
            this.Enlistment.TryMountGVFS(out output).ShouldEqual(false, "GVFS shouldn't mount when index is corrupt");
            output.ShouldContain("Index validation failed");

            this.RepairWithoutConfirmShouldNotFix();

            this.RepairWithConfirmShouldFix();
        }

        [TestCase]
        public void FixesCorruptGitConfig()
        {
            this.Enlistment.UnmountGVFS();

            string gitIndexPath = Path.Combine(this.Enlistment.RepoBackingRoot, ".git", "config");
            File.WriteAllText(gitIndexPath, "[cor");

            this.Enlistment.TryMountGVFS().ShouldEqual(false, "GVFS shouldn't mount when git config is missing");

            this.RepairWithoutConfirmShouldNotFix();

            this.Enlistment.Repair(confirm: true);
            ProcessResult result = GitProcess.InvokeProcess(this.Enlistment.RepoBackingRoot, "remote add origin " + this.Enlistment.RepoUrl);
            result.ExitCode.ShouldEqual(0, result.Errors);
            this.Enlistment.MountGVFS();
        }

        private void CreateCorruptIndexAndRename(string indexPath, Action<FileStream, FileStream> corruptionAction)
        {
            string tempIndexPath = indexPath + ".lock";
            using (FileStream currentIndexStream = new FileStream(indexPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (FileStream tempIndexStream = new FileStream(tempIndexPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite))
            {
                corruptionAction(currentIndexStream, tempIndexStream);
            }

            File.Delete(indexPath);
            File.Move(tempIndexPath, indexPath);
        }

        private void RepairWithConfirmShouldFix()
        {
            this.Enlistment.Repair(confirm: true);
            this.Enlistment.MountGVFS();
        }

        private void RepairWithoutConfirmShouldNotFix()
        {
            this.Enlistment.Repair(confirm: false);
            this.Enlistment.TryMountGVFS().ShouldEqual(false, "Repair without confirm should not fix the enlistment");
        }
    }
}
