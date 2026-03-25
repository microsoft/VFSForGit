using System;

namespace GVFS.Common.NamedPipes
{
    public static partial class NamedPipeMessages
    {
        public static class HydrationStatus
        {
            public const string Request = "GetHydration";
            public const string SuccessResult = "S";
            public const string NotAvailableResult = "NA";

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

                public string ToBody()
                {
                    return string.Join(",",
                        this.PlaceholderFileCount,
                        this.PlaceholderFolderCount,
                        this.ModifiedFileCount,
                        this.ModifiedFolderCount,
                        this.TotalFileCount,
                        this.TotalFolderCount);
                }

                public static bool TryParse(string body, out Response response)
                {
                    response = null;
                    if (string.IsNullOrEmpty(body))
                    {
                        return false;
                    }

                    string[] parts = body.Split(',');
                    if (parts.Length < 6)
                    {
                        return false;
                    }

                    if (!int.TryParse(parts[0], out int placeholderFileCount)
                        || !int.TryParse(parts[1], out int placeholderFolderCount)
                        || !int.TryParse(parts[2], out int modifiedFileCount)
                        || !int.TryParse(parts[3], out int modifiedFolderCount)
                        || !int.TryParse(parts[4], out int totalFileCount)
                        || !int.TryParse(parts[5], out int totalFolderCount))
                    {
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

                    return response.IsValid;
                }
            }
        }
    }
}
