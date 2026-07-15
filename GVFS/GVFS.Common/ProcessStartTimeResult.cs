namespace GVFS.Common
{
    /// <summary>
    /// Outcome of attempting to read a process's start time for lock-holder identity checks.
    /// The distinction between the failure values matters: the orphan-lock detector must only
    /// release a lock when it has positive evidence that the original holder is gone, so it
    /// treats "we could not determine anything" (<see cref="Indeterminate"/>) differently from
    /// evidence that the holder has exited or been replaced.
    /// </summary>
    public enum ProcessStartTimeResult
    {
        /// <summary>
        /// The process is active and its start time was read successfully. The out start time is valid.
        /// </summary>
        Success,

        /// <summary>
        /// Positive evidence that no live process with the requested identity exists: either there is
        /// no process at that PID, or a process was opened but has already exited. Safe to treat the
        /// original holder as gone.
        /// </summary>
        ProcessNotFound,

        /// <summary>
        /// A process exists at the PID but we could not open it (access denied). Because we only
        /// identity-track holders we successfully opened at acquire time (and that access relationship
        /// is stable for a given process's lifetime), an access-denied result here means the PID now
        /// refers to a different process than the original holder.
        /// </summary>
        Inaccessible,

        /// <summary>
        /// A transient or unclassified failure (e.g. resource exhaustion). We genuinely do not know
        /// whether the original holder is alive, so callers must NOT treat this as evidence of exit.
        /// </summary>
        Indeterminate,
    }
}
