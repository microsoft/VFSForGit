using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System;
using System.ComponentModel;
using System.IO;
using System.Text;

namespace GVFS.Common
{
    public class FileBasedLock : IDisposable
    {
        private const int HResultErrorSharingViolation = -2147024864; // -2147024864 = 0x80070020 = ERROR_SHARING_VIOLATION
        private const int HResultErrorFileExists = -2147024816; // -2147024816 = 0x80070050 = ERROR_FILE_EXISTS
        private const int DefaultStreamWriterBufferSize = 1024; // Copied from: http://referencesource.microsoft.com/#mscorlib/system/io/streamwriter.cs,5516ce201dc06b5f
        private const long InvalidFileLength = -1;
        private const string EtwArea = nameof(FileBasedLock);
        private static readonly Encoding UTF8NoBOM = new UTF8Encoding(false, true); // Default encoding used by StreamWriter

        private readonly object deleteOnCloseStreamLock = new object();
        private readonly PhysicalFileSystem fileSystem;
        private readonly string lockPath;
        private ITracer tracer;
        private FileStream deleteOnCloseStream;
        private bool overwriteExistingLock;

        /// <summary>
        /// FileBasedLock constructor
        /// </summary>
        /// <param name="lockPath">Path to lock file</param>
        /// <param name="signature">Text to write in lock file</param>
        /// <param name="cleanupStaleLock">
        /// If true, FileBasedLock constructor will delete the file at lockPath (if one exists on disk)
        /// </param>
        /// <param name="overwriteExistingLock">
        /// If true, FileBasedLock will attempt to overwrite an existing lock file (if one exists on disk) when
        /// acquiring the lock file.
        /// </param>
        /// <remarks>
        /// GVFS keeps an exclusive write handle open to lock files that it creates with FileBasedLock.  This means that 
        /// FileBasedLock still ensures exclusivity when "overwriteExistingLock" is true if the lock file is only used for
        /// coordination between multiple GVFS processes.
        /// </remarks>
        public FileBasedLock(
            PhysicalFileSystem fileSystem, 
            ITracer tracer, 
            string lockPath, 
            string signature, 
            bool cleanupStaleLock,
            bool overwriteExistingLock)
        {
            this.fileSystem = fileSystem;
            this.tracer = tracer;
            this.lockPath = lockPath;
            this.Signature = signature;
            this.overwriteExistingLock = overwriteExistingLock;

            if (cleanupStaleLock)
            {
                this.CleanupStaleLock();
            }
        }

        public string Signature { get; private set; }

        public bool TryAcquireLockAndDeleteOnClose()
        {
            try
            {
                lock (this.deleteOnCloseStreamLock)
                {
                    if (this.IsOpen())
                    {
                        return true;
                    }

                    this.deleteOnCloseStream = (FileStream)this.fileSystem.OpenFileStream(
                        this.lockPath,
                        this.overwriteExistingLock ? FileMode.Create : FileMode.CreateNew,
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
                        this.WriteSignatureAndMessage(writer, message: null);
                    }

                    return true;
                }
            }
            catch (IOException e)
            {
                // HResultErrorFileExists is expected when the lock file exists
                // HResultErrorSharingViolation is expected when the lock file exists and we're in this.overwriteExistingLock mode, as
                // another GVFS process has likely acquired the lock file
                if (e.HResult != HResultErrorFileExists &&
                    !(this.overwriteExistingLock && e.HResult == HResultErrorSharingViolation))
                {
                    EventMetadata metadata = this.CreateLockMetadata(e);
                    this.tracer.RelatedWarning(metadata, "TryAcquireLockAndDeleteOnClose: IOException caught while trying to acquire lock");
                }

                this.DisposeStream();
                return false;
            }
            catch (UnauthorizedAccessException e)
            {
                EventMetadata metadata = this.CreateLockMetadata(e);
                this.tracer.RelatedWarning(metadata, "TryAcquireLockAndDeleteOnClose: UnauthorizedAccessException caught while trying to acquire lock");

                this.DisposeStream();
                return false;
            }
            catch (Win32Exception e)
            {
                EventMetadata metadata = this.CreateLockMetadata(e);
                this.tracer.RelatedWarning(metadata, "TryAcquireLockAndDeleteOnClose: Win32Exception caught while trying to acquire lock");

                this.DisposeStream();
                return false;
            }
            catch (Exception e)
            {
                EventMetadata metadata = this.CreateLockMetadata(e);
                this.tracer.RelatedError(metadata, "TryAcquireLockAndDeleteOnClose: Unhandled exception caught while trying to acquire lock");

                this.DisposeStream();
                throw;
            }
        }

        public bool TryReleaseLock()
        {
            if (this.DisposeStream())
            {
                return true;
            }

            LockData lockData = this.GetLockDataFromDisk();
            if (lockData == null || lockData.Signature != this.Signature)
            {
                if (lockData == null)
                {
                    throw new LockFileDoesNotExistException(this.lockPath);
                }

                throw new LockSignatureDoesNotMatchException(this.lockPath, this.Signature, lockData.Signature);
            }

            try
            {
                this.fileSystem.DeleteFile(this.lockPath);
            }
            catch (IOException e)
            {
                EventMetadata metadata = this.CreateLockMetadata(e);
                this.tracer.RelatedWarning(metadata, "TryReleaseLock: IOException caught while trying to release lock");

                return false;
            }

            return true;
        }

        public bool IsOpen()
        {
            return this.deleteOnCloseStream != null;
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.DisposeStream();
            }
        }

        private LockData GetLockDataFromDisk()
        {
            if (this.LockFileExists())
            {
                string existingSignature;
                string existingMessage;
                this.ReadLockFile(out existingSignature, out existingMessage);
                return new LockData(existingSignature, existingMessage);
            }

            return null;
        }

        private void ReadLockFile(out string existingSignature, out string lockerMessage)
        {
            using (Stream fs = this.fileSystem.OpenFileStream(this.lockPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, callFlushFileBuffers: false))
            using (StreamReader reader = new StreamReader(fs, UTF8NoBOM))
            {
                existingSignature = reader.ReadLine();
                lockerMessage = reader.ReadLine();
            }

            existingSignature = existingSignature ?? string.Empty;
            lockerMessage = lockerMessage ?? string.Empty;
        }

        private bool LockFileExists()
        {
            return this.fileSystem.FileExists(this.lockPath);
        }

        private void CleanupStaleLock()
        {
            if (!this.LockFileExists())
            {
                return;
            }

            long length = InvalidFileLength;
            try
            {
                FileProperties existingLockProperties = this.fileSystem.GetFileProperties(this.lockPath);
                length = existingLockProperties.Length;
            }
            catch (Exception e)
            {
                EventMetadata metadata = this.CreateLockMetadata();
                metadata.Add("Exception", "Exception while getting lock file length: " + e.ToString());
                this.tracer.RelatedEvent(EventLevel.Warning, "CleanupEmptyLock", metadata);
            }

            if (length == 0)
            {
                EventMetadata metadata = this.CreateLockMetadata();
                metadata.Add(TracingConstants.MessageKey.WarningMessage, "Deleting empty lock file: " + this.lockPath);
                this.tracer.RelatedEvent(EventLevel.Warning, "CleanupEmptyLock", metadata);
            }
            else 
            {
                EventMetadata metadata = this.CreateLockMetadata();
                metadata.Add("Length", length == InvalidFileLength ? "Invalid" : length.ToString());
                metadata.Add(TracingConstants.MessageKey.InfoMessage, "Deleting stale lock file: " + this.lockPath);
                this.tracer.RelatedEvent(EventLevel.Informational, "CleanupExistingLock", metadata);
            }

            this.fileSystem.DeleteFile(this.lockPath);
        }

        private void WriteSignatureAndMessage(StreamWriter writer, string message)
        {
            writer.WriteLine(this.Signature);
            if (message != null)
            {
                writer.Write(message);
            }
        }

        private EventMetadata CreateLockMetadata()
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Area", EtwArea);
            metadata.Add("LockPath", this.lockPath);
            metadata.Add("Signature", this.Signature);

            return metadata;
        }

        private EventMetadata CreateLockMetadata(Exception exception = null)
        {
            EventMetadata metadata = this.CreateLockMetadata();
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

        public class LockException : Exception
        {
            public LockException(string messageFormat, params string[] args)
                : base(string.Format(messageFormat, args))
            {
            }
        }

        public class LockFileDoesNotExistException : LockException
        {
            public LockFileDoesNotExistException(string lockPath)
                : base("Lock file {0} does not exist", lockPath)
            {
            }
        }

        public class LockSignatureDoesNotMatchException : LockException
        {
            public LockSignatureDoesNotMatchException(string lockPath, string expectedSignature, string actualSignature)
                : base(
                      "Lock file {0} does not contain expected signature '{1}' (existing signature: '{2}')",
                      lockPath,
                      expectedSignature,
                      actualSignature)
            {
            }
        }

        public class LockData
        {
            public LockData(string signature, string message)
            {
                this.Signature = signature;
                this.Message = message;
            }

            public string Signature { get; }

            public string Message { get; }
        }
    }
}
