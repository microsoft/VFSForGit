using GVFS.Common;
using GVFS.Virtualization.Projection;
using Microsoft.Windows.ProjFS;
using System.Collections.Generic;

namespace GVFS.Platform.Windows
{
    public class ActiveEnumeration
    {
        private static FileNamePatternMatcher doesWildcardPatternMatch = null;

        // Use our own enumerator to avoid having to dispose anything
        private ProjectedFileInfoEnumerator fileInfoEnumerator;
        private FileNamePatternMatcher doesPatternMatch;

        private string filterString = null;

        public ActiveEnumeration(List<ProjectedFileInfo> fileInfos)
        {
            this.fileInfoEnumerator = new ProjectedFileInfoEnumerator(fileInfos);
            this.ResetEnumerator();
            this.MoveNext();
        }

        public delegate bool FileNamePatternMatcher(string name, string pattern);

        /// <summary>
        /// true if Current refers to an element in the enumeration, false if Current is past the end of the collection
        /// </summary>
        public bool IsCurrentValid { get; private set; }

        /// <summary>
        /// Gets the element in the collection at the current position of the enumerator
        /// </summary>
        public ProjectedFileInfo Current
        {
            get { return this.fileInfoEnumerator.Current; }
        }

        /// <summary>
        /// Sets the pattern matching delegate that will be used for file name comparisons when the filter
        /// contains wildcards.
        /// </summary>
        /// <param name="patternMatcher">FileNamePatternMatcher to be used by ActiveEnumeration</param>
        public static void SetWildcardPatternMatcher(FileNamePatternMatcher patternMatcher)
        {
            doesWildcardPatternMatch = patternMatcher;
        }

        /// <summary>
        /// Resets the enumerator and advances it to the first ProjectedFileInfo in the enumeration
        /// </summary>
        /// <param name="filter">Filter string to save.  Can be null.</param>
        public void RestartEnumeration(string filter)
        {
            this.ResetEnumerator();
            this.IsCurrentValid = this.fileInfoEnumerator.MoveNext();
            this.SaveFilter(filter);
        }

        /// <summary>
        /// Advances the enumerator to the next element of the collection (that is being projected).
        /// If a filter string is set, MoveNext will advance to the next entry that matches the filter.
        /// </summary>
        /// <returns>
        /// true if the enumerator was successfully advanced to the next element; false if the enumerator has passed the end of the collection
        /// </returns>
        public bool MoveNext()
        {
            this.IsCurrentValid = this.fileInfoEnumerator.MoveNext();
            while (this.IsCurrentValid && this.IsCurrentHidden())
            {
                this.IsCurrentValid = this.fileInfoEnumerator.MoveNext();
            }

            return this.IsCurrentValid;
        }

        /// <summary>
        /// Attempts to save the filter string for this enumeration.  When setting a filter string, if Current is valid
        /// and does not match the specified filter, the enumerator will be advanced until an element is found that
        /// matches the filter (or the end of the collection is reached).
        /// </summary>
        /// <param name="filter">Filter string to save.  Can be null.</param>
        /// <returns> True if the filter string was saved.  False if the filter string was not saved (because a filter string
        /// was previously saved).
        /// </returns>
        /// <remarks>
        /// Per MSDN (https://msdn.microsoft.com/en-us/library/windows/hardware/ff567047(v=vs.85).aspx, the filter string
        /// specified in the first call to ZwQueryDirectoryFile will be used for all subsequent calls for the handle (and
        /// the string specified in subsequent calls should be ignored)
        /// </remarks>
        public bool TrySaveFilterString(string filter)
        {
            if (this.filterString == null)
            {
                this.SaveFilter(filter);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Returns the current filter string or null if no filter string has been saved
        /// </summary>
        /// <returns>The current filter string or null if no filter string has been saved</returns>
        public string GetFilterString()
        {
            return this.filterString;
        }

        private static bool NameMatchesNoWildcardFilter(string name, string filter)
        {
            return string.Equals(name, filter, GVFSPlatform.Instance.Constants.PathComparison);
        }

        private void SaveFilter(string filter)
        {
            if (string.IsNullOrEmpty(filter))
            {
                this.filterString = string.Empty;
                this.doesPatternMatch = null;
            }
            else
            {
                this.filterString = filter;

                if (Utils.DoesNameContainWildCards(this.filterString))
                {
                    this.doesPatternMatch = doesWildcardPatternMatch;
                }
                else
                {
                    this.doesPatternMatch = NameMatchesNoWildcardFilter;
                }

                if (this.IsCurrentValid && this.IsCurrentHidden())
                {
                    this.MoveNext();
                }
            }
        }

        private bool IsCurrentHidden()
        {
            if (this.doesPatternMatch == null)
            {
                return false;
            }

            return !this.doesPatternMatch(this.Current.Name, this.GetFilterString());
        }

        private void ResetEnumerator()
        {
            this.fileInfoEnumerator.Reset();
        }

        private class ProjectedFileInfoEnumerator
        {
            private List<ProjectedFileInfo> list;
            private int index;

            public ProjectedFileInfoEnumerator(List<ProjectedFileInfo> projectedFileInfos)
            {
                this.list = projectedFileInfos;
                this.Reset();
            }

            public ProjectedFileInfo Current { get; private set; }

            // Combination of the logic in List<T>.Enumerator MoveNext() and MoveNextRare()
            // https://github.com/dotnet/corefx/blob/b492409b4a1952cda4b078f800499d382e1765fc/src/Common/src/CoreLib/System/Collections/Generic/List.cs#L1137
            // (No need to check list._version as GVFS does not modify the lists used for enumeration)
            public bool MoveNext()
            {
                if (this.index < this.list.Count)
                {
                    this.Current = this.list[this.index];
                    this.index++;
                    return true;
                }

                this.index = this.list.Count + 1;
                this.Current = null;
                return false;
            }

            public void Reset()
            {
                this.index = 0;
                this.Current = null;
            }
        }
    }
}
