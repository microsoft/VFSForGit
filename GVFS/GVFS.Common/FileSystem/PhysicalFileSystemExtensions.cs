using GVFS.Common.Tracing;
using System;
using System.IO;
using System.Threading;

namespace GVFS.Common.FileSystem
{
    public static class PhysicalFileSystemExtensions
    {
        /// <summary>
        /// Attempts to delete a file
        /// </summary>
        /// <param name="fileSystem">PhysicalFileSystem</param>
        /// <param name="path">Path of file to delete</param>
        /// <returns>True if the delete succeed, and false otherwise</returns>
        /// <remarks>The files attributes will be set to Normal before deleting the file</remarks>
        public static bool TryDeleteFile(this PhysicalFileSystem fileSystem, string path)
        {
            Exception exception;
            return TryDeleteFile(fileSystem, path, out exception);
        }

        /// <summary>
        /// Attempts to delete a file
        /// </summary>
        /// <param name="fileSystem">PhysicalFileSystem</param>
        /// <param name="path">Path of file to delete</param>
        /// <param name="exception">Exception thrown, if any, while attempting to delete file (or reset file attributes)</param>
        /// <returns>True if the delete succeed, and false otherwise</returns>
        /// <remarks>The files attributes will be set to Normal before deleting the file</remarks>
        public static bool TryDeleteFile(this PhysicalFileSystem fileSystem, string path, out Exception exception)
        {
            exception = null;
            try
            {
                if (fileSystem.FileExists(path))
                {
                    fileSystem.SetAttributes(path, FileAttributes.Normal);
                    fileSystem.DeleteFile(path);
                }

                return true;
            }
            catch (FileNotFoundException)
            {
                // SetAttributes could not find the file
                return true;
            }
            catch (IOException e)
            {
                exception = e;
                return false;
            }
            catch (UnauthorizedAccessException e)
            {
                exception = e;
                return false;
            }
        }

        /// <summary>
        /// Attempts to delete a file
        /// </summary>
        /// <param name="fileSystem">PhysicalFileSystem</param>
        /// <param name="path">Path of file to delete</param>
        /// <param name="metadataKey">Prefix to be used on keys when new entries are added to the metadata</param>
        /// <param name="metadata">Metadata for recording failed deletes</returns>
        /// <remarks>The files attributes will be set to Normal before deleting the file</remarks>
        public static bool TryDeleteFile(this PhysicalFileSystem fileSystem, string path, string metadataKey, EventMetadata metadata)
        {
            Exception deleteException = null;
            if (!TryDeleteFile(fileSystem, path, out deleteException))
            {
                metadata.Add($"{metadataKey}_DeleteFailed", "true");
                if (deleteException != null)
                {
                    metadata.Add($"{metadataKey}_DeleteException", deleteException.ToString());
                }

                return false;
            }

            return true;
        }

        /// <summary>
        /// Retry delete until it succeeds (or maximum number of retries have failed)
        /// </summary>
        /// <param name="fileSystem">PhysicalFileSystem</param>
        /// <param name="tracer">ITracer for logging and telemetry, can be null</param>
        /// <param name="path">Path of file to delete</param>
        /// <param name="retryDelayMs">
        /// Amount of time to wait between each delete attempt.  If 0, there will be no delays between attempts
        /// </param>
        /// <param name="maxRetries">Maximum number of retries (if 0, a single attempt will be made)</param>
        /// <param name="retryLoggingThreshold">
        /// Number of retries to attempt before logging a failure.  First and last failure is always logged if tracer is not null.
        /// </param>
        /// <returns>True if the delete succeed, and false otherwise</returns>
        /// <remarks>The files attributes will be set to Normal before deleting the file</remarks>
        public static bool TryWaitForDelete(
            this PhysicalFileSystem fileSystem,
            ITracer tracer,
            string path,
            int retryDelayMs,
            int maxRetries,
            int retryLoggingThreshold)
        {
            int failureCount = 0;
            while (fileSystem.FileExists(path))
            {
                Exception exception = null;
                if (!TryDeleteFile(fileSystem, path, out exception))
                {
                    if (failureCount == maxRetries)
                    {
                        if (tracer != null)
                        {
                            EventMetadata metadata = new EventMetadata();
                            if (exception != null)
                            {
                                metadata.Add("Exception", exception.ToString());
                            }

                            metadata.Add("path", path);
                            metadata.Add("failureCount", failureCount + 1);
                            metadata.Add("maxRetries", maxRetries);
                            tracer.RelatedWarning(metadata, $"{nameof(TryWaitForDelete)}: Failed to delete file.");
                        }

                        return false;
                    }
                    else
                    {
                        if (tracer != null && failureCount % retryLoggingThreshold == 0)
                        {
                            EventMetadata metadata = new EventMetadata();
                            metadata.Add("Exception", exception.ToString());
                            metadata.Add("path", path);
                            metadata.Add("failureCount", failureCount + 1);
                            metadata.Add("maxRetries", maxRetries);
                            tracer.RelatedWarning(metadata, $"{nameof(TryWaitForDelete)}: Failed to delete file, retrying ...");
                        }
                    }

                    ++failureCount;

                    if (retryDelayMs > 0)
                    {
                        Thread.Sleep(retryDelayMs);
                    }
                }
            }

            return true;
        }
    }
}
