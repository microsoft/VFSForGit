using GVFS.Common;
using GVFS.Common.Tracing;
using GVFS.Platform.Windows;
using System;

namespace GVFS.Service.Handlers
{
    internal static class DevDriveFilterRecovery
    {
        internal static bool TryRecover(
            string volumeRoot,
            ITracer tracer,
            Func<(bool Enabled, string Reason)> readFlag,
            Func<(DevDriveFilterPolicy.AllowFilterResult Result, string Error)> allowFilter,
            Func<(bool Success, bool Rejected, string Error)> attachFilter,
            out string error)
        {
            EventMetadata metadata = new EventMetadata { { "VolumeRoot", volumeRoot } };
            try
            {
                (bool enabled, string reason) = readFlag();
                if (!enabled)
                {
                    metadata.Add("Outcome", "Disabled");
                    metadata.Add("DisabledReason", reason);
                    error = $"The mounted volume rejected PrjFlt. {reason} {GetManualInstructions(volumeRoot)}";
                    return false;
                }

                (DevDriveFilterPolicy.AllowFilterResult result, string policyError) = allowFilter();
                metadata.Add("PolicyResult", result.ToString());
                if (result != DevDriveFilterPolicy.AllowFilterResult.Added
                    && result != DevDriveFilterPolicy.AllowFilterResult.AlreadyAllowed
                    && result != DevDriveFilterPolicy.AllowFilterResult.RemountRequired)
                {
                    metadata.Add("Outcome", "PolicyFailed");
                    metadata.Add("PolicyError", policyError);
                    error = $"The mounted volume rejected PrjFlt. {policyError} {GetManualInstructions(volumeRoot)}";
                    return false;
                }

                (bool success, bool rejected, string attachError) = attachFilter();
                if (success)
                {
                    metadata.Add("Outcome", "Attached");
                    error = null;
                    return true;
                }

                metadata.Add("AttachError", attachError);
                metadata.Add("Outcome", rejected ? "RemountRequired" : "AttachFailed");
                if (!rejected)
                {
                    error = attachError;
                    return false;
                }

                error = $"PrjFlt is allowed on {volumeRoot}, but the mounted volume still uses its previous filter policy. "
                    + "Close applications that use the volume. Remount the volume or restart the computer, then retry gvfs mount. "
                    + "The allowance persists. GVFS did not force-dismount the volume.";
                return false;
            }
            finally
            {
                tracer.RelatedEvent(EventLevel.Informational, "DevDriveFilterRecovery_Summary", metadata, Keywords.Telemetry);
            }
        }

        private static string GetManualInstructions(string volumeRoot)
        {
            return $"From an elevated command prompt, run 'fsutil devdrv query {CommandLineEscaping.EscapeArgument(volumeRoot)}' to inspect the saved allowances. "
                + "After any required update, close applications that use the volume and remount it, or restart the computer.";
        }
    }
}
