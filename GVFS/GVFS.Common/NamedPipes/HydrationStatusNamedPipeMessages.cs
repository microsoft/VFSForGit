using System;
using System.Globalization;

namespace GVFS.Common.NamedPipes
{
    public static partial class NamedPipeMessages
    {
        public static class HydrationStatus
        {
            public const string Request = "GetHydration";
            public const string SuccessResult = "S";
            public const string NotAvailableResult = "NA";

            public enum CacheState
            {
                NotComputed,
                Available,
                ProviderUnavailable,
                FeatureDisabled,
                CircuitBreakerDisabled,
                InvalidCounts,
                IncompleteData,
                Cancelled,
                Failed,
            }

            public enum ParseOutcome
            {
                Success,
                Empty,
                TooFewFields,
                InvalidNumber,
                InvalidCounts,
            }

            public sealed class CacheInfo
            {
                public CacheState State { get; set; }
                public Guid? CalculationId { get; set; }
                public long AgeMs { get; set; } = -1;
                public bool Invalidated { get; set; }
                public bool CountsComplete { get; set; }

                public string ToBody()
                {
                    return string.Join(",", "D1", ((int)this.State).ToString(CultureInfo.InvariantCulture),
                        this.CalculationId?.ToString("N") ?? "-", this.AgeMs.ToString(CultureInfo.InvariantCulture),
                        this.Invalidated ? "1" : "0", this.CountsComplete ? "1" : "0");
                }

                public static bool TryParse(string body, out CacheInfo info, bool hasCounts = false)
                {
                    info = null;
                    if (body == null)
                    {
                        return false;
                    }

                    string[] parts = body.Split(',');
                    int offset = hasCounts ? 6 : 0;
                    if (parts.Length < offset + 6 || parts[offset] != "D1"
                        || !int.TryParse(parts[offset + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int state)
                        || !Enum.IsDefined(typeof(CacheState), state)
                        || (parts[offset + 2] != "-" && !Guid.TryParseExact(parts[offset + 2], "N", out _))
                        || !long.TryParse(parts[offset + 3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long ageMs)
                        || ageMs < -1
                        || (parts[offset + 4] != "0" && parts[offset + 4] != "1")
                        || (parts[offset + 5] != "0" && parts[offset + 5] != "1"))
                    {
                        return false;
                    }

                    info = new CacheInfo
                    {
                        State = (CacheState)state,
                        CalculationId = parts[offset + 2] == "-" ? null : Guid.ParseExact(parts[offset + 2], "N"),
                        AgeMs = ageMs,
                        Invalidated = parts[offset + 4] == "1",
                        CountsComplete = parts[offset + 5] == "1",
                    };
                    return true;
                }
            }

            /// <summary>
            /// Wire format: PlaceholderFileCount,PlaceholderFolderCount,ModifiedFileCount,ModifiedFolderCount,TotalFileCount,TotalFolderCount
            /// </summary>
            public class Response
            {
                public int PlaceholderFileCount { get; set; }
                public int PlaceholderFolderCount { get; set; }
                public int ModifiedFileCount { get; set; }
                public int ModifiedFolderCount { get; set; }
                public int TotalFileCount { get; set; }
                public int TotalFolderCount { get; set; }

                public int HydratedFileCount => this.PlaceholderFileCount + this.ModifiedFileCount;
                public int HydratedFolderCount => this.PlaceholderFolderCount + this.ModifiedFolderCount;

                public bool IsValid =>
                    this.PlaceholderFileCount >= 0
                    && this.PlaceholderFolderCount >= 0
                    && this.ModifiedFileCount >= 0
                    && this.ModifiedFolderCount >= 0
                    && this.TotalFileCount >= this.HydratedFileCount
                    && this.TotalFolderCount >= this.HydratedFolderCount;

                public string ToDisplayMessage()
                {
                    if (!this.IsValid)
                    {
                        return null;
                    }

                    int filePercent = this.TotalFileCount == 0 ? 0 : (int)((100L * this.HydratedFileCount) / this.TotalFileCount);
                    int folderPercent = this.TotalFolderCount == 0 ? 0 : (int)((100L * this.HydratedFolderCount) / this.TotalFolderCount);
                    return $"{filePercent}% of files and {folderPercent}% of folders hydrated. Run 'gvfs health' at the repository root for details.";
                }

                public string ToBody(CacheInfo info = null)
                {
                    string counts = string.Join(",",
                        this.PlaceholderFileCount,
                        this.PlaceholderFolderCount,
                        this.ModifiedFileCount,
                        this.ModifiedFolderCount,
                        this.TotalFileCount,
                        this.TotalFolderCount);
                    return info == null ? counts : counts + "," + info.ToBody();
                }

                public static bool TryParse(string body, out Response response)
                {
                    return TryParse(body, out response, out _);
                }

                public static bool TryParse(string body, out Response response, out ParseOutcome outcome)
                {
                    response = null;
                    outcome = ParseOutcome.Empty;
                    if (string.IsNullOrEmpty(body))
                    {
                        return false;
                    }

                    string[] parts = body.Split(',');
                    if (parts.Length < 6)
                    {
                        outcome = ParseOutcome.TooFewFields;
                        return false;
                    }

                    if (!int.TryParse(parts[0], out int placeholderFileCount)
                        || !int.TryParse(parts[1], out int placeholderFolderCount)
                        || !int.TryParse(parts[2], out int modifiedFileCount)
                        || !int.TryParse(parts[3], out int modifiedFolderCount)
                        || !int.TryParse(parts[4], out int totalFileCount)
                        || !int.TryParse(parts[5], out int totalFolderCount))
                    {
                        outcome = ParseOutcome.InvalidNumber;
                        return false;
                    }

                    response = new Response
                    {
                        PlaceholderFileCount = placeholderFileCount,
                        PlaceholderFolderCount = placeholderFolderCount,
                        ModifiedFileCount = modifiedFileCount,
                        ModifiedFolderCount = modifiedFolderCount,
                        TotalFileCount = totalFileCount,
                        TotalFolderCount = totalFolderCount,
                    };

                    outcome = response.IsValid ? ParseOutcome.Success : ParseOutcome.InvalidCounts;
                    return response.IsValid;
                }
            }
        }
    }
}
