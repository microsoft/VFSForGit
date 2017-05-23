using System;
using System.Collections.Generic;

namespace GVFS.GVFlt
{
    public class GVFltActiveEnumeration : IDisposable
    {
        private readonly IEnumerable<GVFltFileInfo> fileInfos;
        private IEnumerator<GVFltFileInfo> fileInfoEnumerator;
        private bool disposed = false;
        private string filterString = null;

        public GVFltActiveEnumeration(IEnumerable<GVFltFileInfo> fileInfos)
        {
            this.fileInfos = fileInfos;
            this.ResetEnumerator();
            this.MoveNext();
        }

        /// <summary>
        /// true if Current refers to an element in the enumeration, false if Current is past the end of the collection
        /// </summary>
        public bool IsCurrentValid { get; private set; }

        /// <summary>
        /// Gets the element in the collection at the current position of the enumerator
        /// </summary>
        public GVFltFileInfo Current
        {
            get { return this.fileInfoEnumerator.Current; }
        }

        /// <summary>
        /// Resets the enumerator and advances it to the first GVFltFileInfo in the enumeration
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

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposed)
            {
                if (disposing)
                {
                    this.fileInfoEnumerator.Dispose();
                }

                this.disposed = true;
            }
        }

        private static bool FileNameMatchesFilter(string name, string filter)
        {
            if (string.IsNullOrEmpty(filter))
            {
                return true;
            }

            return PatternMatcher.StrictMatchPattern(filter, name);
        }

        private void SaveFilter(string filter)
        {
            if (string.IsNullOrEmpty(filter))
            {
                this.filterString = string.Empty;
            }
            else
            {
                this.filterString = filter;
                if (this.IsCurrentValid && this.IsCurrentHidden())
                {
                    this.MoveNext();
                }
            }
        }

        private bool IsCurrentHidden()
        {
            return !FileNameMatchesFilter(this.Current.Name, this.GetFilterString());
        }

        private void ResetEnumerator()
        {
            this.fileInfoEnumerator = this.fileInfos.GetEnumerator();
        }
    }
}
