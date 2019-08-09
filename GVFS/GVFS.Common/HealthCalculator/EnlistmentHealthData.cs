using System.Collections.Generic;

namespace GVFS.Common
{
    public class EnlistmentHealthData
    {
        public EnlistmentHealthData(
            string targetDirectory,
            int gitFolderCount,
            int gitFileCount,
            int placeholderFolderCount,
            int placeholderFileCount,
            int modifiedPathsFolderCount,
            int modifiedPathsFileCount,
            decimal healthMetric,
            List<EnlistmentHealthCalculator.SubDirectoryInfo> directoryHydrationLevels)
        {
            this.TargetDirectory = targetDirectory;
            this.GitTrackedFolderCount = gitFolderCount;
            this.GitTrackedFileCount = gitFileCount;
            this.PlaceholderFolderCount = placeholderFolderCount;
            this.PlaceholderFileCount = placeholderFileCount;
            this.ModifiedPathsFolderCount = modifiedPathsFolderCount;
            this.ModifiedPathsFileCount = modifiedPathsFileCount;
            this.HealthMetric = healthMetric;
            this.DirectoryHydrationLevels = directoryHydrationLevels;
        }

        public string TargetDirectory { get; private set; }
        public int GitTrackedFolderCount { get; private set; }
        public int GitTrackedFileCount { get; private set; }
        public int GitTrackedItemsCount
        {
            get { return this.GitTrackedFileCount + this.GitTrackedFolderCount; }
        }

        public int PlaceholderFolderCount { get; private set; }
        public int PlaceholderFileCount { get; private set; }
        public int PlaceholderCount
        {
            get { return this.PlaceholderFileCount + this.PlaceholderFolderCount; }
        }

        public int ModifiedPathsFolderCount { get; private set; }
        public int ModifiedPathsFileCount { get; private set; }
        public int ModifiedPathsCount
        {
            get { return this.ModifiedPathsFileCount + this.ModifiedPathsFolderCount; }
        }

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
