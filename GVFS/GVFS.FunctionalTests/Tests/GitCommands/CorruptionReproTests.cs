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
            // Based on FunctionalTests/20170206_Conflict_Source
            const string CherryPickCommit = "51d15f7584e81d59d44c1511ce17d7c493903390";
            const string StartingCommit = "db95d631e379d366d26d899523f8136a77441914";            

            this.ControlGitRepo.Fetch(StartingCommit);
            this.ControlGitRepo.Fetch(CherryPickCommit);

            this.ValidateGitCommand($"checkout -b FunctionalTests/CherryPickRestoreCorruptionRepro {StartingCommit}");

            // Cherry-pick a variety of changes (adds, deletes, modifications) but don't commit.
            // This will leave the repo in a state where the changes are staged (in the index).
            this.ValidateGitCommand($"cherry-pick -n {CherryPickCommit}");

            // Restore --staged should remove the changes from the index but leave them as-is (ie changed)
            // in the working directory.
            // As of VFSForGit 1.0.25169.1 and git 2.50.1.vfs.0.0, the working directory is as-is (still changed,
            // as expected), but the index shows modified/deleted files as unchanged (ie completely reverted)
            // and added files as still staged.
            this.ValidateGitCommand("restore --staged .");
            this.FilesShouldMatchCheckoutOfSourceBranch();
        }
    }
}
