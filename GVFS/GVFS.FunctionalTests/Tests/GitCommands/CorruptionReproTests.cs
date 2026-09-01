using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GVFS.Common;
using GVFS.FunctionalTests.Properties;
using GVFS.FunctionalTests.Tests.EnlistmentPerTestCase;
using NUnit.Framework;

namespace GVFS.FunctionalTests.Tests.GitCommands
{
    /// <summary>
    /// This class is used to reproduce corruption scenarios in the GVFS virtual projection.
    /// </summary>
    [Category(Categories.GitCommands)]
    [TestFixtureSource(typeof(GitRepoTests), nameof(GitRepoTests.ValidateWorkingTree))]
    public class CorruptionReproTests : GitRepoTests
    {
        public CorruptionReproTests(Settings.ValidateWorkingTreeMode validateWorkingTree)
            : base(enlistmentPerTest: true, validateWorkingTree: validateWorkingTree)
        {
        }

        [TestCase]
        public void ReproCherryPickRestoreCorruption()
        {
            // Reproduces a corruption scenario where git commands (like cherry-pick -n)
            // stage changes directly, bypassing the filesystem. In VFS mode, these staged
            // files have skip-worktree set and are not in the ModifiedPaths database.
            // Without the fix, a subsequent "restore --staged" would fail to properly
            // unstage them, leaving the index and projection in an inconsistent state.
            //
            // See https://github.com/microsoft/VFSForGit/issues/1855

            // Based on FunctionalTests/20170206_Conflict_Source
            const string CherryPickCommit = "51d15f7584e81d59d44c1511ce17d7c493903390";
            const string StartingCommit = "db95d631e379d366d26d899523f8136a77441914";

            this.ControlGitRepo.Fetch(StartingCommit);
            this.ControlGitRepo.Fetch(CherryPickCommit);

            this.ValidateGitCommand($"checkout -b FunctionalTests/CherryPickRestoreCorruptionRepro {StartingCommit}");

            // Cherry-pick stages adds, deletes, and modifications without committing.
            // In VFS mode, these changes are made directly by git in the index — they
            // are not in ModifiedPaths, so all affected files still have skip-worktree set.
            this.ValidateGitCommand($"cherry-pick -n {CherryPickCommit}");

            // Restore --staged for a single file first. This verifies that only the
            // targeted file is added to ModifiedPaths, not all staged files (important
            // for performance when there are many staged files, e.g. during merge
            // conflict resolution).
            //
            // Before the fix: added files with skip-worktree would be skipped by
            // restore --staged, remaining stuck as staged in the index.
            this.ValidateGitCommand("restore --staged Test_ConflictTests/AddedFiles/AddedBySource.txt");

            // Restore --staged for everything remaining. Before the fix:
            // - Modified files: restored in the index but invisible to git status
            //   because skip-worktree was set and the file wasn't in ModifiedPaths,
            //   so git never checked the working tree against the index.
            // - Deleted files: same issue — deletions became invisible.
            // - Added files: remained stuck as staged because restore --staged
            //   skipped them (skip-worktree set), and their ProjFS placeholders
            //   would later vanish when the projection reverted to HEAD.
            this.ValidateGitCommand("restore --staged .");

            // Restore the working directory. Before the fix, this step would
            // silently succeed but leave corrupted state: modified/deleted files
            // had stale projected content that didn't match HEAD, and added files
            // (as ProjFS placeholders) would vanish entirely since they're not in
            // HEAD's tree.
            this.ValidateGitCommand("restore -- .");
            this.FilesShouldMatchCheckoutOfSourceBranch();
        }

        /// <summary>
        /// Reproduces a bug where "git reset --mixed" fails to report hydrated files
        /// as modified when skip-worktree hides the working-tree vs index mismatch.
        ///
        /// After a mixed reset, the index is updated to the target commit's tree, but
        /// the working tree is left untouched. For non-hydrated placeholders this is
        /// invisible (ProjFS serves the new content transparently). But for hydrated
        /// files — files that have been read and materialized on disk — the on-disk
        /// content still matches the OLD HEAD. Because the file was never modified
        /// (just read), it is not in ModifiedPaths, so skip-worktree remains set.
        /// This causes "git status" to skip the file entirely, hiding the mismatch
        /// between the stale on-disk content and the new index entry.
        ///
        /// On the FunctionalTests/20201014 branch, Readme.md is the only file that
        /// differs between HEAD and HEAD~1, making it a clean single-file repro.
        ///
        /// Expected: reset output includes "M Readme.md", status shows it as modified.
        /// Actual (bug): reset output is empty, status reports clean.
        /// </summary>
        [TestCase]
        public void ReproResetMixedSkipWorktree()
        {
            // Hydrate Readme.md by reading it via blame. In GVFS, this triggers a
            // ProjFS callback that materializes the file from the object store. The
            // file is now a full file on disk, but NOT in ModifiedPaths (read-only
            // access doesn't modify it), so skip-worktree stays set.
            this.ValidateGitCommand("blame Readme.md");

            this.ValidateGitCommand("checkout -b tests/functional/ReproResetMixedSkipWorktree");

            // Mixed reset to HEAD~1. Readme.md's blob differs between HEAD and HEAD~1
            // on this branch, so the index entry is updated. But the on-disk content
            // still has HEAD's version (mixed reset doesn't touch the working tree).
            //
            // Control repo: reports "M Readme.md" in reset output and status.
            // GVFS (bug): skip-worktree is set and file isn't in ModifiedPaths, so
            // git skips the working-tree check. Reset output omits Readme.md, and
            // status incorrectly reports clean.
            this.ValidateGitCommand("reset HEAD~1");
        }

        /// <summary>
        /// Reproduction of a reported issue:
        /// Restoring a file after its parent directory was deleted fails with
        /// "fatal: could not unlink 'path\to\': Directory not empty"
        ///
        /// See https://github.com/microsoft/VFSForGit/issues/1901
        /// </summary>
        [TestCase]
        public void RestoreAfterDeleteNesteredDirectory()
        {
            // Delete a directory with nested subdirectories and files.
            this.ValidateNonGitCommand("cmd.exe", "/c \"rmdir /s /q GVFlt_DeleteFileTest\"");

            // Restore the working directory.
            this.ValidateGitCommand("restore .");

            this.FilesShouldMatchCheckoutOfSourceBranch();
        }
    }
}
