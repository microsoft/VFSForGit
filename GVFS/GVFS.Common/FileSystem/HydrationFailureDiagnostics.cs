using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GVFS.Common.FileSystem
{
    /// <summary>
    /// Turns an <see cref="IPlatformFileSystem.HydrateFile"/> failure exception into structured,
    /// Kusto-queryable telemetry. Centralized here so every hydration call site (the prefetch
    /// pipeline and the `gvfs prefetch --hydrate` verb) reports the failure identity consistently.
    /// </summary>
    public static class HydrationFailureDiagnostics
    {
        private const int FacilityWin32HResult = unchecked((int)0x80070000);
        private const int FacilityMask = unchecked((int)0xFFFF0000);
        private const int Win32CodeMask = 0xFFFF;

        /// <summary>
        /// Builds per-file EventMetadata describing a hydration failure: exception type, HResult,
        /// and (when derivable) the underlying Win32 error code.
        /// </summary>
        public static EventMetadata BuildMetadata(string path, Exception failure)
        {
            if (failure == null)
            {
                throw new ArgumentNullException(nameof(failure));
            }

            EventMetadata metadata = new EventMetadata
            {
                { "Path", path },
                { "ExceptionType", failure.GetType().Name },
                { "HResult", "0x" + failure.HResult.ToString("X8") },
            };

            int? win32Error = TryGetWin32ErrorCode(failure.HResult);
            if (win32Error.HasValue)
            {
                metadata.Add("Win32Error", win32Error.Value);
            }

            return metadata;
        }

        /// <summary>
        /// Returns a short string identifying the distinct kind of failure (exception type plus
        /// either the Win32 error code or the raw HResult), suitable for aggregating counts across
        /// many failed files without emitting one event per file.
        /// </summary>
        public static string GetFailureSignature(Exception failure)
        {
            if (failure == null)
            {
                throw new ArgumentNullException(nameof(failure));
            }

            int? win32Error = TryGetWin32ErrorCode(failure.HResult);
            return win32Error.HasValue
                ? $"{failure.GetType().Name}(Win32={win32Error.Value})"
                : $"{failure.GetType().Name}(HResult=0x{failure.HResult:X8})";
        }

        /// <summary>
        /// Formats a signature-to-count dictionary as a single string (e.g. "IOException(Win32=32)=3;...")
        /// for inclusion as one EventMetadata field in a summary event.
        /// </summary>
        public static string FormatSignatureCounts(IReadOnlyDictionary<string, int> signatureCounts)
        {
            return string.Join(";", signatureCounts.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        }

        private static int? TryGetWin32ErrorCode(int hresult)
        {
            if ((hresult & FacilityMask) == FacilityWin32HResult)
            {
                return hresult & Win32CodeMask;
            }

            return null;
        }
    }
}
