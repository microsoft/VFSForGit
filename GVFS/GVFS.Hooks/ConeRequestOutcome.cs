namespace GVFS.Hooks
{
    /// <summary>
    /// The result of one cone-management request attempt from a hook.
    /// </summary>
    internal enum ConeRequestOutcome
    {
        /// <summary>The command names no paths, so the hook did nothing.</summary>
        NoPathsNamed,

        /// <summary>The worktree has no cone-mode sparse-checkout file, so the index cannot be sparse and the hook did nothing.</summary>
        NoConeFile,

        /// <summary>The <c>gvfs.auto-sparse-index</c> feature flag is off, so the hook did nothing.</summary>
        FeatureDisabled,

        /// <summary>The command is <c>git stash</c>, which expands the index for any pathspec.</summary>
        SkippedStash,

        /// <summary>The cone covers every named path, so a widen cannot change it.</summary>
        SkippedInCone,

        /// <summary>No named path maps to a cone entry inside the worktree, so the hook did not send a widen.</summary>
        NoResolvablePaths,

        /// <summary>The mount replied that it applied the request.</summary>
        Applied,

        /// <summary>The hook could not connect to the mount.</summary>
        MountUnavailable,

        /// <summary>The mount replied with a result other than success (for example an unknown request).</summary>
        Rejected,

        /// <summary>The mount did not reply within the wait budget.</summary>
        TimedOut,

        /// <summary>The request failed with an exception.</summary>
        Failed,
    }
}
