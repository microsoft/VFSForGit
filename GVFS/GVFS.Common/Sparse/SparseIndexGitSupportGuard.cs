using System;
using System.Collections.Generic;

namespace GVFS.Common.Sparse
{
    /// <summary>
    /// Decides whether a mount must refuse to start, because the enlistment uses the automatic
    /// sparse index and the Git that the mount runs does not support it.
    /// </summary>
    /// <remarks>
    /// A Git without <see cref="AutoSparseIndexConeManager.SparseIndexFeature"/> handles a
    /// cone-mode sparse-checkout file under a virtual file system wrongly. For example,
    /// <c>git add -- &lt;path outside the cone&gt;</c> exits 0 and stages nothing. Git applies a cone,
    /// and writes a sparse index, only with a cone-mode sparse-checkout file. Thus that file and
    /// <c>gvfs.auto-sparse-index</c> are the triggers.
    /// <para>
    /// <c>core.sparseCheckout</c>, <c>index.sparse</c>, and a sparse-checkout file without the
    /// cone header are not triggers. Without a cone-mode file, Git does not use them for a
    /// sparse index. Versions of VFS for Git before 2018 set <c>core.sparseCheckout</c> to
    /// true and wrote a non-cone sparse-checkout file, and such enlistments mount with any
    /// supported Git. A sparse-directory entry in the index is not a trigger either: when the
    /// feature is off, the projection refuses such an index with its own recovery steps.
    /// </para>
    /// </remarks>
    public static class SparseIndexGitSupportGuard
    {
        /// <summary>The number of bytes of the sparse-checkout file that the cone check needs.</summary>
        public const int ConeHeaderReadLength = 64;

        [Flags]
        public enum Triggers
        {
            None = 0,

            /// <summary><c>gvfs.auto-sparse-index</c> is true.</summary>
            AutoSparseIndex = 1,

            /// <summary>The sparse-checkout file has the cone-mode header.</summary>
            ConeFile = 2,
        }

        /// <summary>
        /// Return the states of the enlistment that need a Git with the sparse-index feature.
        /// </summary>
        /// <param name="autoSparseIndex">The value of <c>gvfs.auto-sparse-index</c>.</param>
        /// <param name="coneFileStart">
        /// The start of the sparse-checkout file (at least <see cref="ConeHeaderReadLength"/>
        /// bytes, decoded), or null when the file does not exist.
        /// </param>
        public static Triggers GetTriggers(bool autoSparseIndex, string coneFileStart)
        {
            Triggers triggers = Triggers.None;
            if (autoSparseIndex)
            {
                triggers |= Triggers.AutoSparseIndex;
            }

            if (ConeCoverage.HasConeHeader(coneFileStart))
            {
                triggers |= Triggers.ConeFile;
            }

            return triggers;
        }

        /// <summary>
        /// The mount error for <paramref name="triggers"/>, with the recovery steps.
        /// </summary>
        /// <param name="triggers">The triggers, not <see cref="Triggers.None"/>.</param>
        /// <param name="gitBinPath">The Git that the mount runs.</param>
        /// <param name="gitVersion">The version of that Git.</param>
        /// <param name="sparseCheckoutPath">The full path of the sparse-checkout file.</param>
        public static string GetRefusalMessage(Triggers triggers, string gitBinPath, string gitVersion, string sparseCheckoutPath)
        {
            if (triggers == Triggers.None)
            {
                throw new ArgumentException("There is no trigger", nameof(triggers));
            }

            List<string> reasons = new List<string>();
            if ((triggers & Triggers.AutoSparseIndex) != 0)
            {
                reasons.Add($"{GVFSConstants.GitConfig.AutoSparseIndex} is true");
            }

            if ((triggers & Triggers.ConeFile) != 0)
            {
                reasons.Add($"'{sparseCheckoutPath}' is a cone-mode sparse-checkout file");
            }

            string newLine = Environment.NewLine;
            return
                $"Error: This enlistment uses a sparse index ({string.Join(", ", reasons)}), " +
                $"but the Git that VFS for Git runs ('{gitBinPath}', version {gitVersion}) does not report " +
                $"'{AutoSparseIndexConeManager.SparseIndexFeature}' in 'git version --build-options'. " +
                "With this Git, commands such as 'git add' can skip files outside the sparse-checkout cone." + newLine +
                "To mount, do one of these:" + newLine +
                $"- Install a Git that reports '{AutoSparseIndexConeManager.SparseIndexFeature}', then run 'gvfs mount'." + newLine +
                "- Turn the sparse index off. In the 'src' folder of the enlistment, run these commands, then run 'gvfs mount':" + newLine +
                $"    git -c core.hookspath= config --unset-all {GVFSConstants.GitConfig.AutoSparseIndex}" + newLine +
                "    git -c core.hookspath= config --unset-all index.sparse" + newLine +
                "    git -c core.hookspath= config core.sparseCheckout false" + newLine +
                $"    del \"{sparseCheckoutPath}\"" + newLine +
                "  If 'gvfs mount' then reports a sparse-directory entry in the index, do the steps in that message.";
        }
    }
}
