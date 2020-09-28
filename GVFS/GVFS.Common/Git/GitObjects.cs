using GVFS.Common.FileSystem;
using GVFS.Common.Http;
using GVFS.Common.NetworkStreams;
using GVFS.Common.Tracing;
using ICSharpCode.SharpZipLib;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.Common.Git
{
    public abstract class GitObjects
    {
        protected readonly ITracer Tracer;
        protected readonly GitObjectsHttpRequestor GitObjectRequestor;
        protected readonly Enlistment Enlistment;

        /// <summary>
        /// Used only for testing.
        /// </summary>
        protected bool checkData;

        private const string EtwArea = nameof(GitObjects);
        private const string TempPackFolder = "tempPacks";
        private const string TempIdxExtension = ".tempidx";

        private readonly PhysicalFileSystem fileSystem;

        public GitObjects(ITracer tracer, Enlistment enlistment, GitObjectsHttpRequestor objectRequestor, PhysicalFileSystem fileSystem = null)
        {
            this.Tracer = tracer;
            this.Enlistment = enlistment;
            this.GitObjectRequestor = objectRequestor;
            this.fileSystem = fileSystem ?? new PhysicalFileSystem();
            this.checkData = true;
        }

        public enum DownloadAndSaveObjectResult
        {
            Success,
            ObjectNotOnServer,
            Error
        }

        public static bool IsLooseObjectsDirectory(string value)
        {
            return value.Length == 2 && value.All(c => Uri.IsHexDigit(c));
        }

        public virtual bool TryDownloadCommit(string commitSha)
        {
            const bool PreferLooseObjects = false;
            IEnumerable<string> objectIds = new[] { commitSha };

            GitProcess gitProcess = new GitProcess(this.Enlistment);
            RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.InvocationResult output = this.GitObjectRequestor.TryDownloadObjects(
                objectIds,
                onSuccess: (tryCount, response) => this.TrySavePackOrLooseObject(objectIds, PreferLooseObjects, response, gitProcess),
                onFailure: (eArgs) =>
                {
                    EventMetadata metadata = CreateEventMetadata(eArgs.Error);
                    metadata.Add("Operation", "DownloadAndSaveObjects");
                    metadata.Add("WillRetry", eArgs.WillRetry);

                    if (eArgs.WillRetry)
                    {
                        this.Tracer.RelatedWarning(metadata, eArgs.Error.ToString(), Keywords.Network | Keywords.Telemetry);
                    }
                    else
                    {
                        this.Tracer.RelatedError(metadata, eArgs.Error.ToString(), Keywords.Network);
                    }
                },
                preferBatchedLooseObjects: PreferLooseObjects);

            return output.Succeeded && output.Result.Success;
        }

        public virtual void DeleteStaleTempPrefetchPackAndIdxs()
        {
            string[] staleTempPacks = this.ReadPackFileNames(Path.Combine(this.Enlistment.GitPackRoot, GitObjects.TempPackFolder), GVFSConstants.PrefetchPackPrefix);
            foreach (string stalePackPath in staleTempPacks)
            {
                string staleIdxPath = Path.ChangeExtension(stalePackPath, ".idx");
                string staleTempIdxPath = Path.ChangeExtension(stalePackPath, TempIdxExtension);

                EventMetadata metadata = CreateEventMetadata();
                metadata.Add("stalePackPath", stalePackPath);
                metadata.Add("staleIdxPath", staleIdxPath);
                metadata.Add("staleTempIdxPath", staleTempIdxPath);
                metadata.Add(TracingConstants.MessageKey.InfoMessage, "Deleting stale temp pack and/or idx file");

                this.fileSystem.TryDeleteFile(staleTempIdxPath, metadataKey: nameof(staleTempIdxPath), metadata: metadata);
                this.fileSystem.TryDeleteFile(staleIdxPath, metadataKey: nameof(staleIdxPath), metadata: metadata);
                this.fileSystem.TryDeleteFile(stalePackPath, metadataKey: nameof(stalePackPath), metadata: metadata);

                this.Tracer.RelatedEvent(EventLevel.Informational, nameof(this.DeleteStaleTempPrefetchPackAndIdxs), metadata);
            }
        }

        public virtual void DeleteTemporaryFiles()
        {
            string[] temporaryFiles = this.fileSystem.GetFiles(this.Enlistment.GitPackRoot, "tmp_*");
            foreach (string temporaryFilePath in temporaryFiles)
            {
                EventMetadata metadata = CreateEventMetadata();
                metadata.Add(nameof(temporaryFilePath), temporaryFilePath);
                metadata.Add(TracingConstants.MessageKey.InfoMessage, "Deleting temporary file");

                this.fileSystem.TryDeleteFile(temporaryFilePath, metadataKey: nameof(temporaryFilePath), metadata: metadata);

                this.Tracer.RelatedEvent(EventLevel.Informational, nameof(this.DeleteTemporaryFiles), metadata);
            }
        }

        public virtual bool TryDownloadPrefetchPacks(GitProcess gitProcess, long latestTimestamp, out List<string> packIndexes)
        {
            EventMetadata metadata = CreateEventMetadata();
            metadata.Add("latestTimestamp", latestTimestamp);

            using (ITracer activity = this.Tracer.StartActivity("TryDownloadPrefetchPacks", EventLevel.Informational, Keywords.Telemetry, metadata))
            {
                long bytesDownloaded = 0;

                long requestId = HttpRequestor.GetNewRequestId();
                List<string> innerPackIndexes = null;
                RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.InvocationResult result = this.GitObjectRequestor.TrySendProtocolRequest(
                    requestId: requestId,
                    onSuccess: (tryCount, response) => this.DeserializePrefetchPacks(response, ref latestTimestamp, ref bytesDownloaded, ref innerPackIndexes, gitProcess),
                    onFailure: RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.StandardErrorHandler(activity, requestId, "TryDownloadPrefetchPacks"),
                    method: HttpMethod.Get,
                    endPointGenerator: () => new Uri(
                        string.Format(
                            "{0}?lastPackTimestamp={1}",
                            this.GitObjectRequestor.CacheServer.PrefetchEndpointUrl,
                            latestTimestamp)),
                    requestBodyGenerator: () => null,
                    cancellationToken: CancellationToken.None,
                    acceptType: new MediaTypeWithQualityHeaderValue(GVFSConstants.MediaTypes.PrefetchPackFilesAndIndexesMediaType));

                packIndexes = innerPackIndexes;

                if (!result.Succeeded)
                {
                    if (result.Result != null && result.Result.HttpStatusCodeResult == HttpStatusCode.NotFound)
                    {
                        EventMetadata warning = CreateEventMetadata();
                        warning.Add(TracingConstants.MessageKey.WarningMessage, "The server does not support " + GVFSConstants.Endpoints.GVFSPrefetch);
                        warning.Add(nameof(this.GitObjectRequestor.CacheServer.PrefetchEndpointUrl), this.GitObjectRequestor.CacheServer.PrefetchEndpointUrl);
                        activity.RelatedEvent(EventLevel.Warning, "CommandNotSupported", warning);
                    }
                    else
                    {
                        EventMetadata error = CreateEventMetadata(result.Error);
                        error.Add("latestTimestamp", latestTimestamp);
                        error.Add(nameof(this.GitObjectRequestor.CacheServer.PrefetchEndpointUrl), this.GitObjectRequestor.CacheServer.PrefetchEndpointUrl);
                        activity.RelatedWarning(error, "DownloadPrefetchPacks failed.", Keywords.Telemetry);
                    }
                }

                activity.Stop(new EventMetadata
                    {
                        { "Area", EtwArea },
                        { "Success", result.Succeeded },
                        { "Attempts", result.Attempts },
                        { "BytesDownloaded", bytesDownloaded },
                    });

                return result.Succeeded;
            }
        }

        public virtual string WriteLooseObject(Stream responseStream, string sha, bool overwriteExistingObject, byte[] bufToCopyWith)
        {
            try
            {
                LooseObjectToWrite toWrite = this.GetLooseObjectDestination(sha);

                using (Stream fileStream = this.OpenTempLooseObjectStream(toWrite.TempFile))
                {
                    StreamUtil.CopyToWithBuffer(responseStream, fileStream, bufToCopyWith);
                    fileStream.Flush();
                }

                if (this.checkData)
                {
                    try
                    {
                        using (Stream readFile = this.fileSystem.OpenFileStream(toWrite.TempFile, FileMode.Open, FileAccess.Read, FileShare.Read, true))
                        using (InflaterInputStream inflate = new InflaterInputStream(readFile))
                        using (HashingStream hashing = new HashingStream(inflate))
                        using (NoOpStream devNull = new NoOpStream())
                        {
                            hashing.CopyTo(devNull);

                            string actualSha = SHA1Util.HexStringFromBytes(hashing.Hash);

                            if (!sha.Equals(actualSha, StringComparison.OrdinalIgnoreCase))
                            {
                                string message = $"Requested object with hash {sha} but received object with hash {actualSha}.";
                                message += $"\nFind the incorrect data at '{toWrite.TempFile}'";
                                this.Tracer.RelatedError(message);
                                throw new SecurityException(message);
                            }
                        }
                    }
                    catch (SharpZipBaseException)
                    {
                        string message = $"Requested object with hash {sha} but received data that failed decompression.";
                        message += $"\nFind the incorrect data at '{toWrite.TempFile}'";
                        this.Tracer.RelatedError(message);
                        throw new RetryableException(message);
                    }
                }

                this.FinalizeTempFile(sha, toWrite, overwriteExistingObject);

                return toWrite.ActualFile;
            }
            catch (IOException e)
            {
                throw new RetryableException("IOException while writing loose object. See inner exception for details.", e);
            }
            catch (UnauthorizedAccessException e)
            {
                throw new RetryableException("UnauthorizedAccessException while writing loose object. See inner exception for details.", e);
            }
            catch (Win32Exception e)
            {
                throw new RetryableException("Win32Exception while writing loose object. See inner exception for details.", e);
            }
        }

        public virtual string WriteTempPackFile(Stream stream)
        {
            string fileName = Path.GetRandomFileName();
            string fullPath = Path.Combine(this.Enlistment.GitPackRoot, fileName);

            Task flushTask;
            long fileLength;
            this.TryWriteTempFile(
                tracer: null,
                source: stream,
                tempFilePath: fullPath,
                fileLength: out fileLength,
                flushTask: out flushTask,
                throwOnError: true);

            flushTask?.Wait();

            return fullPath;
        }

        public virtual bool TryWriteTempFile(
            ITracer tracer,
            Stream source,
            string tempFilePath,
            out long fileLength,
            out Task flushTask,
            bool throwOnError = false)
        {
            fileLength = 0;
            flushTask = null;
            try
            {
                Stream fileStream = null;

                try
                {
                    fileStream = this.fileSystem.OpenFileStream(
                        tempFilePath,
                        FileMode.OpenOrCreate,
                        FileAccess.Write,
                        FileShare.Read,
                        callFlushFileBuffers: false); // Any flushing to disk will be done asynchronously

                    StreamUtil.CopyToWithBuffer(source, fileStream);
                    fileLength = fileStream.Length;

                    if (this.Enlistment.FlushFileBuffersForPacks)
                    {
                        // Flush any data buffered in FileStream to the file system
                        fileStream.Flush();

                        // FlushFileBuffers using FlushAsync
                        // Do this last to ensure that the stream is not being accessed after it's been disposed
                        flushTask = fileStream.FlushAsync().ContinueWith((result) => fileStream.Dispose());
                    }
                }
                finally
                {
                    if (flushTask == null && fileStream != null)
                    {
                        fileStream.Dispose();
                    }
                }

                this.ValidateTempFile(tempFilePath, tempFilePath);
            }
            catch (Exception ex)
            {
                if (flushTask != null)
                {
                    flushTask.Wait();
                    flushTask = null;
                }

                this.CleanupTempFile(this.Tracer, tempFilePath);

                if (tracer != null)
                {
                    EventMetadata metadata = CreateEventMetadata(ex);
                    metadata.Add("tempFilePath", tempFilePath);
                    tracer.RelatedWarning(metadata, $"{nameof(this.TryWriteTempFile)}: Exception caught while writing temp file", Keywords.Telemetry);
                }

                if (throwOnError)
                {
                    throw;
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        public virtual GitProcess.Result IndexTempPackFile(string tempPackPath, GitProcess gitProcess = null)
        {
            string packfilePath = GetRandomPackName(this.Enlistment.GitPackRoot);

            Exception moveFileException = null;
            try
            {
                // We're indexing a pack file that was saved to a temp file name, and so it must be renamed
                // to its final name before indexing ('git index-pack' requires that the pack file name end with .pack)
                this.fileSystem.MoveFile(tempPackPath, packfilePath);
            }
            catch (IOException e)
            {
                moveFileException = e;
            }
            catch (UnauthorizedAccessException e)
            {
                moveFileException = e;
            }

            if (moveFileException != null)
            {
                EventMetadata failureMetadata = CreateEventMetadata(moveFileException);
                failureMetadata.Add("tempPackPath", tempPackPath);
                failureMetadata.Add("packfilePath", packfilePath);

                this.fileSystem.TryDeleteFile(tempPackPath, metadataKey: nameof(tempPackPath), metadata: failureMetadata);

                this.Tracer.RelatedWarning(failureMetadata, $"{nameof(this.IndexTempPackFile): Exception caught while trying to move temp pack file}");

                return new GitProcess.Result(
                    string.Empty,
                    moveFileException != null ? moveFileException.Message : "Failed to move temp pack file to final path",
                    GitProcess.Result.GenericFailureCode);
            }

            // TryBuildIndex will delete the pack file if indexing fails
            GitProcess.Result result;
            this.TryBuildIndex(this.Tracer, packfilePath, out result, gitProcess);
            return result;
        }

        public virtual GitProcess.Result IndexPackFile(string packfilePath, GitProcess gitProcess)
        {
            string tempIdxPath = Path.ChangeExtension(packfilePath, TempIdxExtension);
            string idxPath = Path.ChangeExtension(packfilePath, ".idx");

            Exception indexPackException = null;
            try
            {
                if (gitProcess == null)
                {
                    gitProcess = new GitProcess(this.Enlistment);
                }

                GitProcess.Result result = gitProcess.IndexPack(packfilePath, tempIdxPath);
                if (result.ExitCodeIsFailure)
                {
                    Exception exception;
                    if (!this.fileSystem.TryDeleteFile(tempIdxPath, exception: out exception))
                    {
                        EventMetadata metadata = CreateEventMetadata(exception);
                        metadata.Add("tempIdxPath", tempIdxPath);
                        this.Tracer.RelatedWarning(metadata, $"{nameof(this.IndexPackFile)}: Failed to cleanup temp idx file after index pack failure");
                    }
                }
                else
                {
                    if (this.Enlistment.FlushFileBuffersForPacks)
                    {
                        Exception exception;
                        string error;
                        if (!this.TryFlushFileBuffers(tempIdxPath, out exception, out error))
                        {
                            EventMetadata metadata = CreateEventMetadata(exception);
                            metadata.Add("packfilePath", packfilePath);
                            metadata.Add("tempIndexPath", tempIdxPath);
                            metadata.Add("error", error);
                            this.Tracer.RelatedWarning(metadata, $"{nameof(this.IndexPackFile)}: Failed to flush temp idx file buffers");
                        }
                    }

                    this.fileSystem.MoveAndOverwriteFile(tempIdxPath, idxPath);
                }

                return result;
            }
            catch (Win32Exception e)
            {
                indexPackException = e;
            }
            catch (IOException e)
            {
                indexPackException = e;
            }
            catch (UnauthorizedAccessException e)
            {
                indexPackException = e;
            }

            EventMetadata failureMetadata = CreateEventMetadata(indexPackException);
            failureMetadata.Add("packfilePath", packfilePath);
            failureMetadata.Add("tempIdxPath", tempIdxPath);
            failureMetadata.Add("idxPath", idxPath);

            this.fileSystem.TryDeleteFile(tempIdxPath, metadataKey: nameof(tempIdxPath), metadata: failureMetadata);
            this.fileSystem.TryDeleteFile(idxPath, metadataKey: nameof(idxPath), metadata: failureMetadata);

            this.Tracer.RelatedWarning(failureMetadata, $"{nameof(this.IndexPackFile): Exception caught while trying to index pack file}");

            return new GitProcess.Result(
                string.Empty,
                indexPackException != null ? indexPackException.Message : "Failed to index pack file",
                GitProcess.Result.GenericFailureCode);
        }

        public virtual string[] ReadPackFileNames(string packFolderPath, string prefixFilter = "")
        {
            if (this.fileSystem.DirectoryExists(packFolderPath))
            {
                try
                {
                    return this.fileSystem.GetFiles(packFolderPath, prefixFilter + "*.pack");
                }
                catch (DirectoryNotFoundException e)
                {
                    EventMetadata metadata = CreateEventMetadata(e);
                    metadata.Add("packFolderPath", packFolderPath);
                    metadata.Add("prefixFilter", prefixFilter);
                    metadata.Add(TracingConstants.MessageKey.InfoMessage, "${nameof(this.ReadPackFileNames)}: Caught DirectoryNotFoundException exception");
                    this.Tracer.RelatedEvent(EventLevel.Informational, $"{nameof(this.ReadPackFileNames)}_DirectoryNotFound", metadata);

                    return new string[0];
                }
            }

            return new string[0];
        }

        public virtual bool IsUsingCacheServer()
        {
            return !this.GitObjectRequestor.CacheServer.IsNone(this.Enlistment.RepoUrl);
        }

        private static string GetRandomPackName(string packRoot)
        {
            string packName = "pack-" + Guid.NewGuid().ToString("N") + ".pack";
            return Path.Combine(packRoot, packName);
        }

        private static EventMetadata CreateEventMetadata(Exception e = null)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Area", EtwArea);
            if (e != null)
            {
                metadata.Add("Exception", e.ToString());
            }

            return metadata;
        }

        private bool TryMovePackAndIdxFromTempFolder(string packName, string packTempPath, string idxName, string idxTempPath, out Exception exception)
        {
            exception = null;
            string finalPackPath = Path.Combine(this.Enlistment.GitPackRoot, packName);
            string finalIdxPath = Path.Combine(this.Enlistment.GitPackRoot, idxName);

            try
            {
                this.fileSystem.MoveAndOverwriteFile(packTempPath, finalPackPath);
                this.fileSystem.MoveAndOverwriteFile(idxTempPath, finalIdxPath);
            }
            catch (Win32Exception e)
            {
                exception = e;

                EventMetadata metadata = CreateEventMetadata(e);
                metadata.Add("packName", packName);
                metadata.Add("packTempPath", packTempPath);
                metadata.Add("idxName", idxName);
                metadata.Add("idxTempPath", idxTempPath);

                this.fileSystem.TryDeleteFile(idxTempPath, metadataKey: nameof(idxTempPath), metadata: metadata);
                this.fileSystem.TryDeleteFile(finalIdxPath, metadataKey: nameof(finalIdxPath), metadata: metadata);
                this.fileSystem.TryDeleteFile(packTempPath, metadataKey: nameof(packTempPath), metadata: metadata);
                this.fileSystem.TryDeleteFile(finalPackPath, metadataKey: nameof(finalPackPath), metadata: metadata);

                this.Tracer.RelatedWarning(metadata, $"{nameof(this.TryMovePackAndIdxFromTempFolder): Failed to move pack and idx from temp folder}");

                return false;
            }

            return true;
        }

        private bool TryFlushFileBuffers(string path, out Exception exception, out string error)
        {
            error = null;

            FileAttributes originalAttributes;
            if (!this.TryGetAttributes(path, out originalAttributes, out exception))
            {
                error = "Failed to get original attributes, skipping flush";
                return false;
            }

            bool readOnly = (originalAttributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly;

            if (readOnly)
            {
                if (!this.TrySetAttributes(path, originalAttributes & ~FileAttributes.ReadOnly, out exception))
                {
                    error = "Failed to clear read-only attribute, skipping flush";
                    return false;
                }
            }

            bool flushedBuffers = false;
            try
            {
                GVFSPlatform.Instance.FileSystem.FlushFileBuffers(path);
                flushedBuffers = true;
            }
            catch (Win32Exception e)
            {
                exception = e;
                error = "Win32Exception while trying to flush file buffers";
            }

            if (readOnly)
            {
                Exception setAttributesException;
                if (!this.TrySetAttributes(path, originalAttributes, out setAttributesException))
                {
                    EventMetadata metadata = CreateEventMetadata(setAttributesException);
                    metadata.Add("path", path);
                    this.Tracer.RelatedWarning(metadata, $"{nameof(this.TryFlushFileBuffers)}: Failed to re-enable read-only bit");
                }
            }

            return flushedBuffers;
        }

        private bool TryGetAttributes(string path, out FileAttributes attributes, out Exception exception)
        {
            attributes = 0;
            exception = null;
            try
            {
                attributes = this.fileSystem.GetAttributes(path);
                return true;
            }
            catch (IOException e)
            {
                exception = e;
            }
            catch (UnauthorizedAccessException e)
            {
                exception = e;
            }

            return false;
        }

        private bool TrySetAttributes(string path, FileAttributes attributes, out Exception exception)
        {
            exception = null;

            try
            {
                this.fileSystem.SetAttributes(path, attributes);
                return true;
            }
            catch (IOException e)
            {
                exception = e;
            }
            catch (UnauthorizedAccessException e)
            {
                exception = e;
            }

            return false;
        }

        private Stream OpenTempLooseObjectStream(string path)
        {
            return this.fileSystem.OpenFileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                FileOptions.SequentialScan,
                callFlushFileBuffers: false);
        }

        private LooseObjectToWrite GetLooseObjectDestination(string sha)
        {
            // Ensure SHA path is lowercase for case-sensitive filesystems
            if (GVFSPlatform.Instance.Constants.CaseSensitiveFileSystem)
            {
                sha = sha.ToLower();
            }

            string firstTwoDigits = sha.Substring(0, 2);
            string remainingDigits = sha.Substring(2);
            string twoLetterFolderName = Path.Combine(this.Enlistment.GitObjectsRoot, firstTwoDigits);
            this.fileSystem.CreateDirectory(twoLetterFolderName);

            return new LooseObjectToWrite(
                tempFile: Path.Combine(twoLetterFolderName, Path.GetRandomFileName()),
                actualFile: Path.Combine(twoLetterFolderName, remainingDigits));
        }

        /// <summary>
        /// Uses a <see cref="PrefetchPacksDeserializer"/> to read the packs from the stream.
        /// </summary>
        private RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.CallbackResult DeserializePrefetchPacks(
           GitEndPointResponseData response,
           ref long latestTimestamp,
           ref long bytesDownloaded,
           ref List<string> packIndexes,
           GitProcess gitProcess)
        {
            if (packIndexes == null)
            {
                packIndexes = new List<string>();
            }

            using (ITracer activity = this.Tracer.StartActivity("DeserializePrefetchPacks", EventLevel.Informational))
            {
                PrefetchPacksDeserializer deserializer = new PrefetchPacksDeserializer(response.Stream);

                string tempPackFolderPath = Path.Combine(this.Enlistment.GitPackRoot, TempPackFolder);
                this.fileSystem.CreateDirectory(tempPackFolderPath);

                List<TempPrefetchPackAndIdx> tempPacks = new List<TempPrefetchPackAndIdx>();
                foreach (PrefetchPacksDeserializer.PackAndIndex pack in deserializer.EnumeratePacks())
                {
                    // The advertised size may not match the actual, on-disk size.
                    long indexLength = 0;
                    long packLength;

                    // Write the temp and index to a temp folder to avoid putting corrupt files in the pack folder
                    // Once the files are validated and flushed they can be moved to the pack folder
                    string packName = string.Format("{0}-{1}-{2}.pack", GVFSConstants.PrefetchPackPrefix, pack.Timestamp, pack.UniqueId);
                    string packTempPath = Path.Combine(tempPackFolderPath, packName);
                    string idxName = string.Format("{0}-{1}-{2}.idx", GVFSConstants.PrefetchPackPrefix, pack.Timestamp, pack.UniqueId);
                    string idxTempPath = Path.Combine(tempPackFolderPath, idxName);

                    EventMetadata data = CreateEventMetadata();
                    data["timestamp"] = pack.Timestamp.ToString();
                    data["uniqueId"] = pack.UniqueId;
                    activity.RelatedEvent(EventLevel.Informational, "Receiving Pack/Index", data);

                    // Write the pack
                    // If it fails, TryWriteTempFile cleans up the file and we retry the prefetch
                    Task packFlushTask;
                    if (!this.TryWriteTempFile(activity, pack.PackStream, packTempPath, out packLength, out packFlushTask))
                    {
                        bytesDownloaded += packLength;
                        return new RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.CallbackResult(null, true);
                    }

                    bytesDownloaded += packLength;

                    // We will try to build an index if the server does not send one
                    if (pack.IndexStream == null)
                    {
                        GitProcess.Result result;
                        if (!this.TryBuildIndex(activity, packTempPath, out result, gitProcess))
                        {
                            if (packFlushTask != null)
                            {
                                packFlushTask.Wait();
                            }

                            // Move whatever has been successfully downloaded so far
                            Exception moveException;
                            this.TryFlushAndMoveTempPacks(tempPacks, ref latestTimestamp, out moveException);

                            return new RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.CallbackResult(null, true);
                        }

                        tempPacks.Add(new TempPrefetchPackAndIdx(pack.Timestamp, packName, packTempPath, packFlushTask, idxName, idxTempPath, idxFlushTask: null));
                    }
                    else
                    {
                        Task indexFlushTask;
                        if (this.TryWriteTempFile(activity, pack.IndexStream, idxTempPath, out indexLength, out indexFlushTask))
                        {
                            tempPacks.Add(new TempPrefetchPackAndIdx(pack.Timestamp, packName, packTempPath, packFlushTask, idxName, idxTempPath, indexFlushTask));
                        }
                        else
                        {
                            bytesDownloaded += indexLength;

                            // Try to build the index manually, then retry the prefetch
                            GitProcess.Result result;
                            if (this.TryBuildIndex(activity, packTempPath, out result, gitProcess))
                            {
                                // If we were able to recreate the failed index
                                // we can start the prefetch at the next timestamp
                                tempPacks.Add(new TempPrefetchPackAndIdx(pack.Timestamp, packName, packTempPath, packFlushTask, idxName, idxTempPath, idxFlushTask: null));
                            }
                            else
                            {
                                if (packFlushTask != null)
                                {
                                    packFlushTask.Wait();
                                }
                            }

                            // Move whatever has been successfully downloaded so far
                            Exception moveException;
                            this.TryFlushAndMoveTempPacks(tempPacks, ref latestTimestamp, out moveException);

                            // The download stream will not be in a good state if the index download fails.
                            // So we have to restart the prefetch
                            return new RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.CallbackResult(null, true);
                        }
                    }

                    bytesDownloaded += indexLength;
                }

                Exception exception = null;
                if (!this.TryFlushAndMoveTempPacks(tempPacks, ref latestTimestamp, out exception))
                {
                    return new RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.CallbackResult(exception, true);
                }

                foreach (TempPrefetchPackAndIdx tempPack in tempPacks)
                {
                    packIndexes.Add(tempPack.IdxName);
                }

                return new RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.CallbackResult(
                    new GitObjectsHttpRequestor.GitObjectTaskResult(success: true));
            }
        }

        private bool TryFlushAndMoveTempPacks(List<TempPrefetchPackAndIdx> tempPacks, ref long latestTimestamp, out Exception exception)
        {
            exception = null;
            bool moveFailed = false;
            foreach (TempPrefetchPackAndIdx tempPack in tempPacks)
            {
                if (tempPack.PackFlushTask != null)
                {
                    tempPack.PackFlushTask.Wait();
                }

                if (tempPack.IdxFlushTask != null)
                {
                    tempPack.IdxFlushTask.Wait();
                }

                // If we've hit a failure moving temp files, we should stop trying to move them (but we still need to wait for all outstanding
                // flush tasks)
                if (!moveFailed)
                {
                    if (this.TryMovePackAndIdxFromTempFolder(tempPack.PackName, tempPack.PackFullPath, tempPack.IdxName, tempPack.IdxFullPath, out exception))
                    {
                        latestTimestamp = tempPack.Timestamp;
                    }
                    else
                    {
                        moveFailed = true;
                    }
                }
            }

            return !moveFailed;
        }

        /// <summary>
        /// Attempts to build an index for the specified path.  If building the index fails, the pack file is deleted
        /// </summary>
        private bool TryBuildIndex(
            ITracer activity,
            string packFullPath,
            out GitProcess.Result result,
            GitProcess gitProcess)
        {
            result = this.IndexPackFile(packFullPath, gitProcess);

            if (result.ExitCodeIsFailure)
            {
                EventMetadata errorMetadata = CreateEventMetadata();
                Exception exception;
                if (!this.fileSystem.TryDeleteFile(packFullPath, exception: out exception))
                {
                    if (exception != null)
                    {
                        errorMetadata.Add("deleteException", exception.ToString());
                    }

                    errorMetadata.Add("deletedBadPack", "false");
                }

                errorMetadata.Add("Operation", nameof(this.TryBuildIndex));
                errorMetadata.Add("packFullPath", packFullPath);
                activity.RelatedWarning(errorMetadata, result.Errors, Keywords.Telemetry);
            }

            return result.ExitCodeIsSuccess;
        }

        private void CleanupTempFile(ITracer activity, string fullPath)
        {
            Exception e;
            if (!this.fileSystem.TryDeleteFile(fullPath, exception: out e))
            {
                EventMetadata info = CreateEventMetadata(e);
                info.Add("file", fullPath);
                activity.RelatedWarning(info, "Failed to cleanup temp file");
            }
        }

        private void FinalizeTempFile(string sha, LooseObjectToWrite toWrite, bool overwriteExistingObject)
        {
            try
            {
                // Checking for existence reduces warning outputs when a streamed download tries.
                if (this.fileSystem.FileExists(toWrite.ActualFile))
                {
                    if (overwriteExistingObject)
                    {
                        EventMetadata metadata = CreateEventMetadata();
                        metadata.Add("file", toWrite.ActualFile);
                        metadata.Add("tempFile", toWrite.TempFile);
                        metadata.Add(TracingConstants.MessageKey.InfoMessage, $"{nameof(this.FinalizeTempFile)}: Overwriting existing loose object");
                        this.Tracer.RelatedEvent(EventLevel.Informational, $"{nameof(this.FinalizeTempFile)}_OverwriteExistingObject", metadata);

                        this.ValidateTempFile(toWrite.TempFile, sha);
                        this.fileSystem.MoveAndOverwriteFile(toWrite.TempFile, toWrite.ActualFile);
                    }
                }
                else
                {
                    this.ValidateTempFile(toWrite.TempFile, sha);

                    try
                    {
                        this.fileSystem.MoveFile(toWrite.TempFile, toWrite.ActualFile);
                    }
                    catch (IOException ex)
                    {
                        // IOExceptions happen when someone else is writing to our object.
                        // That implies they are doing what we're doing, which should be a success
                        EventMetadata info = CreateEventMetadata(ex);
                        info.Add("file", toWrite.ActualFile);
                        this.Tracer.RelatedWarning(info, $"{nameof(this.FinalizeTempFile)}: Exception moving temp file");
                    }
                }
            }
            finally
            {
                this.CleanupTempFile(this.Tracer, toWrite.TempFile);
            }
        }

        private void ValidateTempFile(string tempFilePath, string finalFilePath)
        {
            using (Stream fs = this.fileSystem.OpenFileStream(tempFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, callFlushFileBuffers: false))
            {
                if (fs.Length == 0)
                {
                    throw new RetryableException($"Temp file '{tempFilePath}' for '{finalFilePath}' was written with 0 bytes");
                }
                else
                {
                    byte[] buffer = new byte[10];

                    // Temp files should always have at least one non-zero byte
                    int bytesRead = fs.Read(buffer, 0, buffer.Length);
                    if (buffer.All(b => b == 0))
                    {
                        RetryableException ex = new RetryableException(
                            $"Temp file '{tempFilePath}' for '{finalFilePath}' was written with {bytesRead} null bytes");

                        EventMetadata eventInfo = CreateEventMetadata(ex);
                        eventInfo.Add("file", tempFilePath);
                        eventInfo.Add("finalFilePath", finalFilePath);
                        this.Tracer.RelatedWarning(eventInfo, $"{nameof(this.ValidateTempFile)}: Temp file invalid");

                        throw ex;
                    }
                }
            }
        }

        private RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.CallbackResult TrySavePackOrLooseObject(
                                                                                                IEnumerable<string> objectShas,
                                                                                                bool unpackObjects,
                                                                                                GitEndPointResponseData responseData,
                                                                                                GitProcess gitProcess)
        {
            if (responseData.ContentType == GitObjectContentType.LooseObject)
            {
                List<string> objectShaList = objectShas.Distinct().ToList();
                if (objectShaList.Count != 1)
                {
                    return new RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.CallbackResult(new InvalidOperationException("Received loose object when multiple objects were requested."), shouldRetry: false);
                }

                // To reduce allocations, reuse the same buffer when writing objects in this batch
                byte[] bufToCopyWith = new byte[StreamUtil.DefaultCopyBufferSize];

                this.WriteLooseObject(responseData.Stream, objectShaList[0], overwriteExistingObject: false, bufToCopyWith: bufToCopyWith);
            }
            else if (responseData.ContentType == GitObjectContentType.BatchedLooseObjects)
            {
                // To reduce allocations, reuse the same buffer when writing objects in this batch
                byte[] bufToCopyWith = new byte[StreamUtil.DefaultCopyBufferSize];

                BatchedLooseObjectDeserializer deserializer = new BatchedLooseObjectDeserializer(
                    responseData.Stream,
                    (stream, sha) => this.WriteLooseObject(stream, sha, overwriteExistingObject: false, bufToCopyWith: bufToCopyWith));
                deserializer.ProcessObjects();
            }
            else
            {
                GitProcess.Result result = this.TryAddPackFile(responseData.Stream, unpackObjects, gitProcess);
                if (result.ExitCodeIsFailure)
                {
                    return new RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.CallbackResult(new InvalidOperationException("Could not add pack file: " + result.Errors), shouldRetry: false);
                }
            }

            return new RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.CallbackResult(new GitObjectsHttpRequestor.GitObjectTaskResult(true));
        }

        private GitProcess.Result TryAddPackFile(Stream contents, bool unpackObjects, GitProcess gitProcess)
        {
            GitProcess.Result result;

            this.fileSystem.CreateDirectory(this.Enlistment.GitPackRoot);

            if (unpackObjects)
            {
                result = new GitProcess(this.Enlistment).UnpackObjects(contents);
            }
            else
            {
                string tempPackPath = this.WriteTempPackFile(contents);
                return this.IndexTempPackFile(tempPackPath, gitProcess);
            }

            return result;
        }

        private struct LooseObjectToWrite
        {
            public readonly string TempFile;
            public readonly string ActualFile;

            public LooseObjectToWrite(string tempFile, string actualFile)
            {
                this.TempFile = tempFile;
                this.ActualFile = actualFile;
            }
        }

        private class TempPrefetchPackAndIdx
        {
            public TempPrefetchPackAndIdx(
                long timestamp,
                string packName,
                string packFullPath,
                Task packFlushTask,
                string idxName,
                string idxFullPath,
                Task idxFlushTask)
            {
                this.Timestamp = timestamp;
                this.PackName = packName;
                this.PackFullPath = packFullPath;
                this.PackFlushTask = packFlushTask;
                this.IdxName = idxName;
                this.IdxFullPath = idxFullPath;
                this.IdxFlushTask = idxFlushTask;
            }

            public long Timestamp { get; }
            public string PackName { get; }
            public string PackFullPath { get; }
            public Task PackFlushTask { get; }
            public string IdxName { get; }
            public string IdxFullPath { get; }
            public Task IdxFlushTask { get; }
        }
    }
}
