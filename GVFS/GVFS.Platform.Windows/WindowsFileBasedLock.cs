using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using System;
using System.ComponentModel;
using System.IO;
using System.Text;

namespace GVFS.Platform.Windows
{
    public class WindowsFileBasedLock : FileBasedLock
    {
        private const int HResultErrorSharingViolation = -2147024864; // -2147024864 = 0x80070020 = ERROR_SHARING_VIOLATION
        private const int HResultErrorFileExists = -2147024816; // -2147024816 = 0x80070050 = ERROR_FILE_EXISTS
        private const int DefaultStreamWriterBufferSize = 1024; // Copied from: http://referencesource.microsoft.com/#mscorlib/system/io/streamwriter.cs,5516ce201dc06b5f
        private const string EtwArea = nameof(WindowsFileBasedLock);
        private static readonly Encoding UTF8NoBOM = new UTF8Encoding(false, true); // Default encoding used by StreamWriter

        private readonly object deleteOnCloseStreamLock = new object();
        private Stream deleteOnCloseStream;

        /// <summary>
        /// FileBasedLock constructor
        /// </summary>
        /// <param name="lockPath">Path to lock file</param>
        /// <param name="signature">Text to write in lock file</param>
        /// <remarks>
        /// GVFS keeps an exclusive write handle open to lock files that it creates with FileBasedLock.  This means that
        /// FileBasedLock still ensures exclusivity when the lock file is used only for coordination between multiple GVFS processes.
        /// </remarks>
        public WindowsFileBasedLock(
            PhysicalFileSystem fileSystem,
            ITracer tracer,
            string lockPath)
            : base(fileSystem, tracer, lockPath)
        {
        }

        public override bool TryAcquireLock()
        {
            try
            {
                lock (this.deleteOnCloseStreamLock)
                {
                    if (this.deleteOnCloseStream != null)
                    {
                        throw new InvalidOperationException("Lock has already been acquired");
                    }

                    this.FileSystem.CreateDirectory(Path.GetDirectoryName(this.LockPath));

                    this.deleteOnCloseStream = this.FileSystem.OpenFileStream(
                        this.LockPath,
                        FileMode.Create,
                        FileAccess.ReadWrite,
                        FileShare.Read,
                        FileOptions.DeleteOnClose,
                        callFlushFileBuffers: false);

                    return true;
                }
            }
            catch (IOException e)
            {
                // HResultErrorFileExists is expected when the lock file exists
                // HResultErrorSharingViolation is expected when the lock file exists andanother GVFS process has acquired the lock file
                if (e.HResult != HResultErrorFileExists && e.HResult != HResultErrorSharingViolation)
                {
                    EventMetadata metadata = this.CreateLockMetadata(e);
                    this.Tracer.RelatedWarning(metadata, $"{nameof(this.TryAcquireLock)}: IOException caught while trying to acquire lock");
                }

                this.DisposeStream();
                return false;
            }
            catch (UnauthorizedAccessException e)
            {
                EventMetadata metadata = this.CreateLockMetadata(e);
                this.Tracer.RelatedWarning(metadata, $"{nameof(this.TryAcquireLock)}: UnauthorizedAccessException caught while trying to acquire lock");

                this.DisposeStream();
                return false;
            }
            catch (Win32Exception e)
            {
                EventMetadata metadata = this.CreateLockMetadata(e);
                this.Tracer.RelatedWarning(metadata, $"{nameof(this.TryAcquireLock)}: Win32Exception caught while trying to acquire lock");

                this.DisposeStream();
                return false;
            }
            catch (Exception e)
            {
                EventMetadata metadata = this.CreateLockMetadata(e);
                this.Tracer.RelatedError(metadata, $"{nameof(this.TryAcquireLock)}: Unhandled exception caught while trying to acquire lock");

                this.DisposeStream();
                throw;
            }
        }

        public override void Dispose()
        {
            this.DisposeStream();
        }

        private EventMetadata CreateLockMetadata(Exception exception = null)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Area", EtwArea);
            metadata.Add(nameof(this.LockPath), this.LockPath);
            if (exception != null)
            {
                metadata.Add("Exception", exception.ToString());
            }

            return metadata;
        }

        private bool DisposeStream()
        {
            lock (this.deleteOnCloseStreamLock)
            {
                if (this.deleteOnCloseStream != null)
                {
                    this.deleteOnCloseStream.Dispose();
                    this.deleteOnCloseStream = null;
                    return true;
                }
            }

            return false;
        }
    }
}
