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
    }
}
