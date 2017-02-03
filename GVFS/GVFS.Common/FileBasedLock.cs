using GVFS.Common.Physical.FileSystem;
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
        private const int DefaultStreamWriterBufferSize = 1024; // Copied from: http://referencesource.microsoft.com/#mscorlib/system/io/streamwriter.cs,5516ce201dc06b5f
        private const long InvalidFileLength = -1;
        private static readonly Encoding UTF8NoBOM = new UTF8Encoding(false, true); // Default encoding used by StreamWriter

        private readonly object deleteOnCloseStreamLock = new object();
        private readonly PhysicalFileSystem fileSystem;
        private readonly string lockPath;
        private ITracer tracer;
        private FileStream deleteOnCloseStream;

        public FileBasedLock(PhysicalFileSystem fileSystem, ITracer tracer, string lockPath, string signature, ExistingLockCleanup existingLockCleanup)
        {
            this.fileSystem = fileSystem;
            this.tracer = tracer;
            this.lockPath = lockPath;
            this.Signature = signature;

            if (existingLockCleanup != ExistingLockCleanup.LeaveExisting)
            {
                this.CleanupStaleLock(existingLockCleanup);
            }
        }

        public enum ExistingLockCleanup
        {
            LeaveExisting,
            DeleteExisting,
            DeleteExistingAndLogSignature
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
                        FileMode.CreateNew,
                        (FileAccess)(NativeMethods.FileAccess.FILE_GENERIC_READ | NativeMethods.FileAccess.FILE_GENERIC_WRITE | NativeMethods.FileAccess.DELETE),
                        NativeMethods.FileAttributes.FILE_FLAG_DELETE_ON_CLOSE,
                        FileShare.Read);

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
            catch (NativeMethods.Win32FileExistsException)
            {
                this.DisposeStream();
                return false;
            }
            catch (IOException e)
            {
                EventMetadata metadata = this.CreateLockMetadata("IOException caught while trying to acquire lock", e);
                this.tracer.RelatedEvent(EventLevel.Warning, "TryAcquireLockAndDeleteOnClose", metadata);

                this.DisposeStream();
                return false;
            }
            catch (Win32Exception e)
            {
                EventMetadata metadata = this.CreateLockMetadata("Win32Exception caught while trying to acquire lock", e);
                this.tracer.RelatedEvent(EventLevel.Warning, "TryAcquireLockAndDeleteOnClose", metadata);

                this.DisposeStream();
                return false;
            }
            catch (Exception e)
            {
                EventMetadata metadata = this.CreateLockMetadata("Unhandled exception caught while trying to acquire lock", e);
                this.tracer.RelatedError("TryAcquireLockAndDeleteOnClose", metadata);

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
                EventMetadata metadata = this.CreateLockMetadata("IOException caught while trying to release lock", e);
                this.tracer.RelatedEvent(EventLevel.Warning, "TryReleaseLock", metadata);

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
            this.DisposeStream();
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
            using (Stream fs = this.fileSystem.OpenFileStream(this.lockPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
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

        private void CleanupStaleLock(ExistingLockCleanup existingLockCleanup)
        {
            if (!this.LockFileExists())
            {
                return;
            }

            if (existingLockCleanup == ExistingLockCleanup.LeaveExisting)
            {
                throw new ArgumentException("CleanupStaleLock should not be called with LeaveExisting");
            }

            EventMetadata metadata = this.CreateLockMetadata();
            metadata.Add("existingLockCleanup", existingLockCleanup.ToString());

            long length = InvalidFileLength;
            try
            {
                FileProperties existingLockProperties = this.fileSystem.GetFileProperties(this.lockPath);
                length = existingLockProperties.Length;
            }
            catch (Exception e)
            {
                metadata.Add("Exception", "Exception while getting lock file length: " + e.ToString());
                this.tracer.RelatedEvent(EventLevel.Warning, "CleanupEmptyLock", metadata);
            }

            if (length == 0)
            {
                metadata.Add("Message", "Deleting empty lock file: " + this.lockPath);
                this.tracer.RelatedEvent(EventLevel.Warning, "CleanupEmptyLock", metadata);
            }
            else 
            {
                metadata.Add("Length", length == InvalidFileLength ? "Invalid" : length.ToString());

                switch (existingLockCleanup)
                {
                    case ExistingLockCleanup.DeleteExisting:
                        metadata.Add("Message", "Deleting stale lock file: " + this.lockPath);
                        this.tracer.RelatedEvent(EventLevel.Informational, "CleanupExistingLock", metadata);
                        break;

                    case ExistingLockCleanup.DeleteExistingAndLogSignature:
                        string existingSignature;
                        try
                        {
                            string dummyLockerMessage;
                            this.ReadLockFile(out existingSignature, out dummyLockerMessage);
                        }
                        catch (Win32Exception e)
                        {
                            if (e.ErrorCode == NativeMethods.ERROR_FILE_NOT_FOUND)
                            {
                                // File was deleted before we could read its contents
                                return;
                            }

                            throw;
                        }

                        if (existingSignature == this.Signature)
                        {
                            metadata.Add("Message", "Deleting stale lock file: " + this.lockPath);
                            this.tracer.RelatedEvent(EventLevel.Informational, "CleanupExistingLock", metadata);
                        }
                        else
                        {
                            metadata.Add("ExistingLockSignature", existingSignature);
                            metadata.Add("Message", "Deleting stale lock file: " + this.lockPath + " with mismatched signature");
                            this.tracer.RelatedEvent(EventLevel.Warning, "CleanupSignatureMismatchLock", metadata);
                        }

                        break;

                    default:
                        throw new InvalidOperationException("Invalid ExistingLockCleanup");
                }                
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

        private EventMetadata CreateLockMetadata(string message = null, Exception exception = null, bool errorMessage = false)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Area", "FileBasedLock");
            metadata.Add("LockPath", this.lockPath);
            metadata.Add("Signature", this.Signature);

            if (message != null)
            {
                metadata.Add(errorMessage ? "ErrorMessage" : "Message", message);
            }

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
