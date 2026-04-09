using GVFS.Common.Tracing;
using System;
using System.IO;

namespace GVFS.Common
{
    /// <summary>
    /// Tracks hydration status computation failures and auto-disables the feature
    /// after repeated failures to protect users from persistent performance issues.
    /// 
    /// The circuit breaker resets when:
    /// - A new calendar day begins (UTC)
    /// - The GVFS version changes (indicating an update that may fix the issue)
    /// 
    /// This class intentionally avoids dependencies on PhysicalFileSystem so it can
    /// be file-linked into lightweight projects like GVFS.Hooks.
    /// </summary>
    public class HydrationStatusCircuitBreaker
    {
        public const int MaxFailuresPerDay = 3;

        private readonly string markerFilePath;
        private readonly ITracer tracer;

        public HydrationStatusCircuitBreaker(
            string dotGVFSRoot,
            ITracer tracer)
        {
            this.markerFilePath = Path.Combine(
                dotGVFSRoot,
                GVFSConstants.DotGVFS.HydrationStatus.DisabledMarkerFile);
            this.tracer = tracer;
        }

        /// <summary>
        /// Returns true if the hydration status feature should be skipped due to
        /// too many recent failures.
        /// </summary>
        public bool IsDisabled()
        {
            try
            {
                if (!File.Exists(this.markerFilePath))
                {
                    return false;
                }

                string content = File.ReadAllText(this.markerFilePath);
                if (!TryParseMarkerFile(content, out string markerDate, out string markerVersion, out int failureCount))
                {
                    return false;
                }

                string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
                string currentVersion = ProcessHelper.GetCurrentProcessVersion();

                // Stale marker from a previous day or version — not disabled.
                // RecordFailure will reset the count when it next runs.
                if (markerDate != today || markerVersion != currentVersion)
                {
                    return false;
                }

                return failureCount >= MaxFailuresPerDay;
            }
            catch (Exception ex)
            {
                this.tracer.RelatedWarning($"Error reading hydration status circuit breaker: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Records a failure. After <see cref="MaxFailuresPerDay"/> failures in a day,
        /// the circuit breaker trips and <see cref="IsDisabled"/> returns true.
        /// Uses exclusive file access to prevent concurrent processes from losing counts.
        /// </summary>
        public void RecordFailure()
        {
            try
            {
                int failureCount = 1;
                string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
                string currentVersion = ProcessHelper.GetCurrentProcessVersion();

                Directory.CreateDirectory(Path.GetDirectoryName(this.markerFilePath));

                // Use exclusive file access to prevent concurrent read-modify-write races.
                // If another process holds the file, we skip this failure rather than block.
                try
                {
                    using (FileStream fs = new FileStream(
                        this.markerFilePath,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None))
                    {
                        string existingContent;
                        using (StreamReader reader = new StreamReader(fs, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 256, leaveOpen: true))
                        {
                            existingContent = reader.ReadToEnd();
                        }

                        if (TryParseMarkerFile(existingContent, out string markerDate, out string markerVersion, out int existingCount)
                            && markerDate == today
                            && markerVersion == currentVersion)
                        {
                            failureCount = existingCount + 1;
                        }

                        // Reset to beginning and write new content
                        fs.Position = 0;
                        fs.SetLength(0);
                        using (StreamWriter writer = new StreamWriter(fs))
                        {
                            writer.Write($"{today}\n{currentVersion}\n{failureCount}");
                        }
                    }
                }
                catch (IOException)
                {
                    // Another process holds the file — skip this failure count
                    return;
                }

                if (failureCount >= MaxFailuresPerDay)
                {
                    this.tracer.RelatedWarning(
                        $"Hydration status circuit breaker tripped after {failureCount} failures today. " +
                        $"Feature will be disabled until tomorrow or a GVFS update.");
                }
            }
            catch (Exception ex)
            {
                this.tracer.RelatedWarning($"Error writing hydration status circuit breaker: {ex.Message}");
            }
        }

        /// <summary>
        /// Parses the marker file format: date\nversion\ncount
        /// </summary>
        internal static bool TryParseMarkerFile(string content, out string date, out string version, out int failureCount)
        {
            date = null;
            version = null;
            failureCount = 0;

            if (string.IsNullOrEmpty(content))
            {
                return false;
            }

            string[] lines = content.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 3)
            {
                return false;
            }

            date = lines[0];
            version = lines[1];
            return int.TryParse(lines[2], out failureCount);
        }
    }
}
