using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using System;
using System.ComponentModel;
using System.IO;
using System.Text;

namespace GVFS.Platform.Windows
{
    public class WindowsFileBasedLock : IFileBasedLock
    {
        private const int HResultErrorSharingViolation = -2147024864; // -2147024864 = 0x80070020 = ERROR_SHARING_VIOLATION
        private const int HResultErrorFileExists = -2147024816; // -2147024816 = 0x80070050 = ERROR_FILE_EXISTS
        private const int DefaultStreamWriterBufferSize = 1024; // Copied from: http://referencesource.microsoft.com/#mscorlib/system/io/streamwriter.cs,5516ce201dc06b5f
        private const string EtwArea = nameof(WindowsFileBasedLock);
        private static readonly Encoding UTF8NoBOM = new UTF8Encoding(false, true); // Default encoding used by StreamWriter

        private readonly object deleteOnCloseStreamLock = new object();
        private readonly PhysicalFileSystem fileSystem;
        private readonly string lockPath;
        private ITracer tracer;
        private Stream deleteOnCloseStream;
        private string signature;

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
            string lockPath,
            string signature)
        {
            this.fileSystem = fileSystem;
            this.tracer = tracer;
            this.lockPath = lockPath;
            this.signature = signature;
        }

        public bool TryAcquireLock()
        {
            try
            {
                lock (this.deleteOnCloseStreamLock)
                {
                    if (this.IsOpen())
                    {
                        return true;
                    }

                    this.fileSystem.CreateDirectory(Path.GetDirectoryName(this.lockPath));

                    this.deleteOnCloseStream = this.fileSystem.OpenFileStream(
                        this.lockPath,
                        FileMode.Create,
                        FileAccess.ReadWrite,
                        FileShare.Read,
                        FileOptions.DeleteOnClose,
                        callFlushFileBuffers: false);

                    // Pass in true for leaveOpen to ensure that lockStream stays open
                    using (StreamWriter writer = new StreamWriter(
                        this.deleteOnCloseStream,
                        UTF8NoBOM,
                        DefaultStreamWriterBufferSize,
                        leaveOpen: true))
                    {
                        this.WriteSignature(writer);
                    }

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
                    this.tracer.RelatedWarning(metadata, $"{nameof(this.TryAcquireLock)}: IOException caught while trying to acquire lock");
                }

                this.DisposeStream();
                return false;
            }
            catch (UnauthorizedAccessException e)
            {
                EventMetadata metadata = this.CreateLockMetadata(e);
                this.tracer.RelatedWarning(metadata, $"{nameof(this.TryAcquireLock)}: UnauthorizedAccessException caught while trying to acquire lock");

                this.DisposeStream();
                return false;
            }
            catch (Win32Exception e)
            {
                EventMetadata metadata = this.CreateLockMetadata(e);
                this.tracer.RelatedWarning(metadata, $"{nameof(this.TryAcquireLock)}: Win32Exception caught while trying to acquire lock");

                this.DisposeStream();
                return false;
            }
            catch (Exception e)
            {
                EventMetadata metadata = this.CreateLockMetadata(e);
                this.tracer.RelatedError(metadata, $"{nameof(this.TryAcquireLock)}: Unhandled exception caught while trying to acquire lock");

                this.DisposeStream();
                throw;
            }
        }

        public void Dispose()
        {
            this.DisposeStream();
        }

        private bool IsOpen()
        {
            return this.deleteOnCloseStream != null;
        }

        private void WriteSignature(StreamWriter writer)
        {
            writer.WriteLine(this.signature);
        }

        private EventMetadata CreateLockMetadata(Exception exception = null)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Area", EtwArea);
            metadata.Add("LockPath", this.lockPath);
            metadata.Add("Signature", this.signature);
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
