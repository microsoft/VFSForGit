using System;

namespace GVFS.Service
{
    public sealed class InstallationTelemetryRecord
    {
        public int SchemaVersion { get; set; }
        public Guid AttemptId { get; set; }
        public Guid EventId { get; set; }
        public string EventType { get; set; }
        public DateTimeOffset OccurredUtc { get; set; }
        public string InstallerVersion { get; set; }
        public string PreviousVersion { get; set; }
        public string PackageArchitecture { get; set; }
        public string OSArchitecture { get; set; }
        public string Mode { get; set; }
        public string Outcome { get; set; }
        public string LastPhase { get; set; }
        public string RemountOutcome { get; set; }
        public int ScriptResult { get; set; }

        public bool IsValid(DateTimeOffset now)
        {
            return this.SchemaVersion == 1
                && this.AttemptId != Guid.Empty
                && this.EventId != Guid.Empty
                && (this.EventType == "InstallationStarted" || this.EventType == "InstallationFinished")
                && this.OccurredUtc.Offset == TimeSpan.Zero
                && this.OccurredUtc >= DateTimeOffset.UnixEpoch
                && this.OccurredUtc <= now.AddMinutes(5)
                && Version.TryParse(this.InstallerVersion, out Version installerVersion)
                && (this.PreviousVersion == "Unknown" || Version.TryParse(this.PreviousVersion, out Version previousVersion))
                && IsOneOf(this.PackageArchitecture, "x64", "arm64")
                && IsOneOf(this.OSArchitecture, "x64", "arm64")
                && IsOneOf(this.Mode, "Unknown", "Fresh", "Immediate", "Staged")
                && IsOneOf(this.Outcome, "Started", "Completed", "Staged", "Cancelled", "Incomplete", "Failed")
                && IsOneOf(this.LastPhase, "Prepare", "Install", "PostInstall", "Service", "Remount", "Done")
                && IsOneOf(this.RemountOutcome, "NotRequested", "Succeeded", "Failed", "Unknown")
                && (this.ScriptResult == 0 || this.ScriptResult == 17);
        }

        private static bool IsOneOf(string value, params string[] choices)
        {
            return Array.IndexOf(choices, value) >= 0;
        }
    }
}
