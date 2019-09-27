using System.Collections.Generic;

namespace GVFS.Common
{
    public class EnlistmentHealthData
    {
        public EnlistmentHealthData(
            string targetDirectory,
            int gitItemsCount,
            int placeholderCount,
            int modifiedPathsCount,
            decimal healthMetric,
            List<EnlistmentHealthCalculator.SubDirectoryInfo> directoryHydrationLevels)
        {
            this.TargetDirectory = targetDirectory;
            this.GitTrackedItemsCount = gitItemsCount;
            this.PlaceholderCount = placeholderCount;
            this.ModifiedPathsCount = modifiedPathsCount;
            this.HealthMetric = healthMetric;
            this.DirectoryHydrationLevels = directoryHydrationLevels;
        }

        public string TargetDirectory { get; private set; }
        public int GitTrackedItemsCount { get; private set; }
        public int PlaceholderCount { get; private set; }
        public int ModifiedPathsCount { get; private set; }
        public List<EnlistmentHealthCalculator.SubDirectoryInfo> DirectoryHydrationLevels { get; private set; }
        public decimal HealthMetric { get; private set; }
        public decimal PlaceholderPercentage
        {
            get
            {
                if (this.GitTrackedItemsCount == 0)
                {
                    return 0;
                }

                return (decimal)this.PlaceholderCount / this.GitTrackedItemsCount;
            }
        }

        public decimal ModifiedPathsPercentage
        {
            get
            {
                if (this.GitTrackedItemsCount == 0)
                {
                    return 0;
                }

                return (decimal)this.ModifiedPathsCount / this.GitTrackedItemsCount;
            }
        }
    }
}
