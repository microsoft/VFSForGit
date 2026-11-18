using System;
using System.IO;

namespace GVFS.Common.Sparse
{
    /// <summary>
    /// Resolves the per-worktree <c>info/sparse-checkout</c> file path for a GVFS
    /// enlistment. The cone file is worktree-scoped, so a linked worktree must write
    /// its own file, not the shared one.
    /// </summary>
    /// <remarks>
    /// A linked worktree's sparse-checkout file is in its per-worktree git dir
    /// (<c>.git/worktrees/&lt;name&gt;/info/sparse-checkout</c>), not in the shared git dir.
    /// For a linked worktree, <see cref="GVFSEnlistment.DotGitRoot"/> is the shared git dir,
    /// so it must not be used to find the cone file. The primary enlistment uses
    /// <c>.git/info/sparse-checkout</c>. Git finds the same per-worktree location through
    /// <c>git_pathdup</c>, which sends <c>info/sparse-checkout</c> to the worktree git dir.
    /// </remarks>
    public static class SparseCheckoutPathResolver
    {
        /// <summary>
        /// Resolve the sparse-checkout file path for an enlistment. Use the per-worktree git
        /// dir for a linked worktree, and the shared git dir otherwise.
        /// </summary>
        public static string GetSparseCheckoutFilePath(GVFSEnlistment enlistment)
        {
            ArgumentNullException.ThrowIfNull(enlistment);

            return GetSparseCheckoutFilePath(
                enlistment.IsWorktree,
                enlistment.DotGitRoot,
                enlistment.IsWorktree ? enlistment.Worktree.WorktreeGitDir : null);
        }

        /// <summary>
        /// Resolve the sparse-checkout file path from primitive inputs. When
        /// <paramref name="isWorktree"/> is true, the file is under
        /// <paramref name="worktreeGitDir"/>. Otherwise it is under
        /// <paramref name="dotGitRoot"/> (the shared <c>.git</c> directory).
        /// </summary>
        public static string GetSparseCheckoutFilePath(bool isWorktree, string dotGitRoot, string worktreeGitDir)
        {
            string gitDir = isWorktree ? worktreeGitDir : dotGitRoot;
            if (string.IsNullOrEmpty(gitDir))
            {
                throw new ArgumentException(
                    isWorktree
                        ? "worktreeGitDir must be provided for a worktree enlistment."
                        : "dotGitRoot must be provided for a primary enlistment.");
            }

            return Path.Combine(gitDir, GVFSConstants.DotGit.Info.Name, GVFSConstants.DotGit.Info.SparseCheckoutName);
        }
    }
}
