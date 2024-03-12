using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Prefetch;
using GVFS.Common.Tracing;
using System;
using System.IO;

namespace FastFetch
{
    /// <summary>
    ///   A mechanism for holding the 'index.lock' on a repository for the time it takes to update the index
    ///   and working tree.  It attempts to create the file in the constructor and throws if that fails.
    ///   It closes and deletes index.lock on dispose.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///     This class should not have to exist.  If FastFetch was in compliance with the git way of doing
    ///     business, then <see cref="CheckoutPrefetcher"/> would work like this:
    ///   </para>
    ///   <list type="bullet">
    ///     <item>
    ///       It would open index.lock like this does - with CreateNew, before it started messing with the working tree.
    ///     </item>
    ///     <item>
    ///       It would have just one class responsible for writing the new index into index.lock (now it has two,
    ///       <see cref="Index"/> and <see cref="GitIndexGenerator"/>).  And this combined class would write in the
    ///       file size and timestamp information from the appropriate sources as it goes.
    ///     </item>
    ///     <item>
    ///       It would then reread index.lock (without closing it) and calculate the hash.
    ///     </item>
    ///     <item>
    ///       It would then delete the old index file, close index.lock, and move it to index.
    ///     </item>
    ///   </list>
    ///   <para>
    ///     This is all in contrast to how it works now, where it has separate operations for updating
    ///     the working tree, creating an index with no size/timestamp information, and then rewriting
    ///     it with that information.
    ///   </para>
    ///   <para>
    ///     This class is just a bodge job to make it so that we can leave the code pretty much as-is (and reduce
    ///     the risk of breaking things) and still get the protection we need against simultaneous git commands
    ///     being run.
    ///   </para>
    /// </remarks>
    public class IndexLock
        : IDisposable
    {
        private string lockFilePath;
        private FileStream lockFileStream;

        public IndexLock(string repositoryRoot, ITracer tracer)
        {
            this.lockFilePath = Path.Combine(repositoryRoot, GVFSConstants.DotGit.IndexLock);
            try
            {
                this.lockFileStream = File.Open(lockFilePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            }
            catch (Exception ex)
            {
                tracer.RelatedError("Unable to create: {0}: {1}", lockFilePath, ex.Message);
                throw new BlobPrefetcher.FetchException("Could not acquire index.lock.");
            }
        }

        /// <inheritdoc/>>
        public void Dispose()
        {
            if (this.lockFilePath == null)
            {
                return;
            }

            if (this.lockFileStream == null)
            {
                throw new ObjectDisposedException(nameof(IndexLock));
            }

            this.lockFileStream.Dispose();
            this.lockFileStream = null;

            File.Delete(this.lockFilePath);
            this.lockFilePath = null;
        }
    }
}
