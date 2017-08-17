using GVFS.Common.Http;
using GVFS.Common.NetworkStreams;
using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace GVFS.Common.Git
{
    public class GitObjects
    {
        protected readonly ITracer Tracer;
        protected readonly Enlistment Enlistment;
        protected readonly GitObjectsHttpRequestor GitObjectRequestor;

        private const string AreaPath = "GitObjects";

        public GitObjects(ITracer tracer, Enlistment enlistment, GitObjectsHttpRequestor objectRequestor)
        {
            this.Tracer = tracer;
            this.Enlistment = enlistment;
            this.GitObjectRequestor = objectRequestor;
        }

        public enum DownloadAndSaveObjectResult
        {
            Success,
            ObjectNotOnServer,
            Error
        }

        public virtual bool TryDownloadAndSaveCommit(string commitSha, int commitDepth)
        {
            return this.TryDownloadAndSaveObjects(new string[] { commitSha }, commitDepth, preferLooseObjects: false);
        }

        public bool TryDownloadAndSaveBlobs(IEnumerable<string> blobShas)
        {
            return this.TryDownloadAndSaveObjects(blobShas, commitDepth: 1, preferLooseObjects: true);
        }

        public bool TryDownloadPrefetchPacks(long latestTimestamp)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("latestTimestamp", latestTimestamp);

            using (ITracer activity = this.Tracer.StartActivity(nameof(this.TryDownloadPrefetchPacks), EventLevel.Informational, metadata))
            {
                long requestId = HttpRequestor.GetNewRequestId();
                RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.InvocationResult result = this.GitObjectRequestor.TrySendProtocolRequest(
                    requestId: requestId,
                    onSuccess: (tryCount, response) => this.DeserializePrefetchPacks(response, ref latestTimestamp),
                    onFailure: RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.StandardErrorHandler(activity, requestId, nameof(this.TryDownloadPrefetchPacks)),
                    method: HttpMethod.Get,
                    endPointGenerator: () => new Uri(
                        string.Format(
                            "{0}?lastPackTimestamp={1}",
                            this.GitObjectRequestor.CacheServer.PrefetchEndpointUrl,
                            latestTimestamp)),
                    requestBodyGenerator: () => null,
                    acceptType: new MediaTypeWithQualityHeaderValue(GVFSConstants.MediaTypes.PrefetchPackFilesAndIndexesMediaType));

                if (!result.Succeeded)
                {
                    if (result.Result != null && result.Result.HttpStatusCodeResult == HttpStatusCode.NotFound)
                    {
                        EventMetadata warning = new EventMetadata();
                        warning.Add("ErrorMessage", "The server does not support " + GVFSConstants.Endpoints.GVFSPrefetch);
                        warning.Add(nameof(this.GitObjectRequestor.CacheServer.PrefetchEndpointUrl), this.GitObjectRequestor.CacheServer.PrefetchEndpointUrl);
                        activity.RelatedEvent(EventLevel.Warning, "CommandNotSupported", warning);
                    }
                    else
                    {
                        EventMetadata error = new EventMetadata();
                        error.Add("latestTimestamp", latestTimestamp);
                        error.Add("Exception", result.Error);
                        error.Add("ErrorMessage", "DownloadPrefetchPacks failed.");
                        error.Add(nameof(this.GitObjectRequestor.CacheServer.PrefetchEndpointUrl), this.GitObjectRequestor.CacheServer.PrefetchEndpointUrl);
                        activity.RelatedError(error);
                    }
                }

                return result.Succeeded;
            }
        }

        public virtual string WriteLooseObject(Stream responseStream, string sha, byte[] bufToCopyWith)
        {
            LooseObjectToWrite toWrite = this.GetLooseObjectDestination(sha);

            using (Stream fileStream = OpenTempLooseObjectStream(toWrite.TempFile, async: false))
            {
                StreamUtil.CopyToWithBuffer(responseStream, fileStream, bufToCopyWith);
            }

            this.FinalizeTempFile(sha, toWrite);

            return toWrite.ActualFile;
        }

        public virtual string WriteTempPackFile(GitEndPointResponseData response)
        {
            string fileName = Path.GetRandomFileName();
            string fullPath = Path.Combine(this.Enlistment.GitPackRoot, fileName);

            this.TryWriteNamedPackOrIdx(
                tracer: null,
                source: response.Stream,
                targetFullPath: fullPath,
                throwOnError: true);
            return fullPath;
        }

        public virtual bool TryWriteNamedPackOrIdx(
            ITracer tracer,
            Stream source,
            string targetFullPath,
            bool throwOnError = false)
        {
            // It is important to write temp files then rename so that git
            // does not mistake a half-written file for an invalid one.
            string tempPath = targetFullPath + "temp";

            try
            {
                using (Stream fileStream = File.OpenWrite(tempPath))
                {
                    StreamUtil.CopyToWithBuffer(source, fileStream);
                }

                this.ValidateTempFile(tempPath, targetFullPath);
                File.Move(tempPath, targetFullPath);
            }
            catch (Exception ex)
            {
                this.CleanupTempFile(this.Tracer, tempPath);

                if (tracer != null)
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Exception", ex.ToString());
                    metadata.Add("ErrorMessage", "Exception caught while writing pack or index");
                    metadata.Add("TargetFullPath", targetFullPath);
                    tracer.RelatedError(metadata);
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

        public virtual GitProcess.Result IndexTempPackFile(string tempPackPath)
        {
            string packfilePath = GetRandomPackName(this.Enlistment.GitPackRoot);
            return this.IndexTempPackFile(tempPackPath, packfilePath);
        }

        public virtual GitProcess.Result IndexTempPackFile(string tempPackPath, string packfilePath)
        {
            try
            {
                File.Move(tempPackPath, packfilePath);

                GitProcess.Result result = new GitProcess(this.Enlistment).IndexPack(packfilePath);
                if (result.HasErrors)
                {
                    File.Delete(packfilePath);
                }

                return result;
            }
            catch (Exception e)
            {
                if (File.Exists(packfilePath))
                {
                    File.Delete(packfilePath);
                }

                if (File.Exists(tempPackPath))
                {
                    File.Delete(tempPackPath);
                }

                return new GitProcess.Result(string.Empty, e.Message, GitProcess.Result.GenericFailureCode);
            }
        }

        public virtual string[] ReadPackFileNames(string prefixFilter = "")
        {
            return Directory.GetFiles(this.Enlistment.GitPackRoot, prefixFilter + "*.pack");
        }

        protected virtual DownloadAndSaveObjectResult TryDownloadAndSaveObject(string objectSha, int maxAttempts)
        {
            if (objectSha == GVFSConstants.AllZeroSha)
            {
                return DownloadAndSaveObjectResult.Error;
            }

            // To reduce allocations, reuse the same buffer when writing objects in this batch
            byte[] bufToCopyWith = new byte[StreamUtil.DefaultCopyBufferSize];

            RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.InvocationResult output = this.GitObjectRequestor.TryDownloadLooseObject(
                objectSha,
                maxAttempts,
                onSuccess: (tryCount, response) =>
                {
                    this.WriteLooseObject(response.Stream, objectSha, bufToCopyWith);
                    return new RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.CallbackResult(new GitObjectsHttpRequestor.GitObjectTaskResult(true));
                });

            if (output.Succeeded && output.Result.Success)
            {
                return DownloadAndSaveObjectResult.Success;
            }

            if (output.Result != null && output.Result.HttpStatusCodeResult == HttpStatusCode.NotFound)
            {
                return DownloadAndSaveObjectResult.ObjectNotOnServer;
            }

            return DownloadAndSaveObjectResult.Error;
        }

        private static string GetRandomPackName(string packRoot)
        {
            string packName = "pack-" + Guid.NewGuid().ToString("N") + ".pack";
            return Path.Combine(packRoot, packName);
        }

        private static FileStream OpenTempLooseObjectStream(string path, bool async)
        {
            FileOptions options = FileOptions.SequentialScan;
            if (async)
            {
                options |= FileOptions.Asynchronous;
            }

            return new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096, // .NET Default
                options: options);
        }

        private LooseObjectToWrite GetLooseObjectDestination(string sha)
        {
            string firstTwoDigits = sha.Substring(0, 2);
            string remainingDigits = sha.Substring(2);
            string twoLetterFolderName = Path.Combine(this.Enlistment.GitObjectsRoot, firstTwoDigits);
            Directory.CreateDirectory(twoLetterFolderName);

            return new LooseObjectToWrite(
                tempFile: Path.Combine(twoLetterFolderName, Path.GetRandomFileName()),
                actualFile: Path.Combine(twoLetterFolderName, remainingDigits));
        }

        private bool TryDownloadAndSaveObjects(IEnumerable<string> objectIds, int commitDepth, bool preferLooseObjects)
        {
            RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.InvocationResult output = this.GitObjectRequestor.TryDownloadObjects(
                objectIds,
                commitDepth,
                onSuccess: (tryCount, response) => this.TrySavePackOrLooseObject(objectIds, preferLooseObjects, response),
                onFailure: (eArgs) =>
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Operation", "DownloadAndSaveObjects");
                    metadata.Add("WillRetry", eArgs.WillRetry);
                    metadata.Add("ErrorMessage", eArgs.Error.ToString());
                    this.Tracer.RelatedError(metadata, Keywords.Network);
                },
                preferBatchedLooseObjects: preferLooseObjects);

            return output.Succeeded && output.Result.Success;
        }

        /// <summary>
        /// Uses a <see cref="PrefetchPacksDeserializer"/> to read the packs from the stream.
        /// </summary>
        private RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.CallbackResult DeserializePrefetchPacks(
           GitEndPointResponseData response, ref long latestTimestamp)
        {
            using (ITracer activity = this.Tracer.StartActivity(nameof(this.DeserializePrefetchPacks), EventLevel.Informational))
            {
                PrefetchPacksDeserializer deserializer = new PrefetchPacksDeserializer(response.Stream);

                foreach (PrefetchPacksDeserializer.PackAndIndex pack in deserializer.EnumeratePacks())
                {
                    string packName = string.Format("{0}-{1}-{2}.pack", GVFSConstants.PrefetchPackPrefix, pack.Timestamp, pack.UniqueId);
                    string packFullPath = Path.Combine(this.Enlistment.GitPackRoot, packName);
                    string idxName = string.Format("{0}-{1}-{2}.idx", GVFSConstants.PrefetchPackPrefix, pack.Timestamp, pack.UniqueId);
                    string idxFullPath = Path.Combine(this.Enlistment.GitPackRoot, idxName);

                    EventMetadata data = new EventMetadata();
                    data["timestamp"] = pack.Timestamp.ToString();
                    data["uniqueId"] = pack.UniqueId;
                    activity.RelatedEvent(EventLevel.Informational, "Receiving Pack/Index", data);

                    // Write the pack
                    // If it fails, TryWriteNamedPackOrIdx cleans up the packfile and we retry the prefetch
                    if (!this.TryWriteNamedPackOrIdx(activity, pack.PackStream, packFullPath))
                    {
                        return new RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.CallbackResult(null, true);
                    }

                    // We will try to build an index if the server does not send one
                    if (pack.IndexStream == null)
                    {
                        if (!this.TryBuildIndex(activity, pack, packFullPath))
                        {
                            return new RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.CallbackResult(null, true);
                        }
                    }
                    else if (!this.TryWriteNamedPackOrIdx(activity, pack.IndexStream, idxFullPath))
                    {
                        // Try to build the index manually, then retry the prefetch
                        if (this.TryBuildIndex(activity, pack, packFullPath))
                        {
                            // If we were able to recreate the failed index
                            // we can start the prefetch at the next timestamp
                            latestTimestamp = pack.Timestamp;
                        }

                        // The download stream will not be in a good state if the index download fails.
                        // So we have to restart the prefetch
                        return new RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.CallbackResult(null, true);
                    }

                    latestTimestamp = pack.Timestamp;
                }

                return new RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.CallbackResult(
                    new GitObjectsHttpRequestor.GitObjectTaskResult(true));
            }
        }

        private bool TryBuildIndex(
            ITracer activity,
            PrefetchPacksDeserializer.PackAndIndex pack,
            string packFullPath)
        {
            GitProcess.Result result = this.IndexTempPackFile(packFullPath, Path.ChangeExtension(packFullPath, ".pack"));

            if (result.HasErrors)
            {
                // IndexTempPackFile will delete the bad temp pack for us.
                EventMetadata errorMetadata = new EventMetadata();
                errorMetadata.Add("Operation", "TryBuildIndex");
                errorMetadata.Add("pack", packFullPath);
                errorMetadata.Add("ErrorMessage", result.Errors);
                activity.RelatedError(errorMetadata);
            }

            return !result.HasErrors;
        }

        private void CleanupTempFile(ITracer activity, string packRoot, string file)
        {
            if (file == null)
            {
                return;
            }

            string fullPath = Path.Combine(packRoot, file);
            this.CleanupTempFile(activity, fullPath);
        }

        private void CleanupTempFile(ITracer activity, string fullPath)
        {
            try
            {
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }
            }
            catch (IOException failedDelete)
            {
                EventMetadata info = new EventMetadata();
                info.Add("ErrorMessage", "Exception cleaning up temp file");
                info.Add("file", fullPath);
                info.Add("Exception", failedDelete.ToString());
                activity.RelatedEvent(EventLevel.Warning, "Warning", info);
            }
        }

        private void FinalizeTempFile(string sha, LooseObjectToWrite toWrite)
        {
            try
            {
                // Checking for existence reduces warning outputs when a streamed download tries.
                if (!File.Exists(toWrite.ActualFile))
                {
                    this.ValidateTempFile(toWrite.TempFile, sha);

                    try
                    {
                        File.Move(toWrite.TempFile, toWrite.ActualFile);
                    }
                    catch (IOException ex)
                    {
                        // IOExceptions happen when someone else is writing to our object.
                        // That implies they are doing what we're doing, which should be a success
                        EventMetadata info = new EventMetadata();
                        info.Add("Message", "Exception moving temp file");
                        info.Add("file", toWrite.ActualFile);
                        info.Add("Exception", ex.Message);
                        this.Tracer.RelatedEvent(EventLevel.Warning, "Warning", info);
                    }
                }
            }
            finally
            {
                this.CleanupTempFile(this.Tracer, toWrite.TempFile);
            }
        }

        private void ValidateTempFile(string filePath, string intendedPurpose)
        {
            FileInfo info = new FileInfo(filePath);
            if (info.Length == 0)
            {
                throw new RetryableException("Temp file for '" + intendedPurpose + "' was written with 0 bytes");
            }
            else
            {
                using (Stream fs = info.OpenRead())
                {
                    byte[] buffer = new byte[10];

                    // This will always read at least one non-zero byte
                    int bytesRead = fs.Read(buffer, 0, buffer.Length);
                    if (buffer.All(b => b == 0))
                    {
                        RetryableException ex = new RetryableException(
                            "Temp file for '" + intendedPurpose + "' was written with " + bytesRead + " null bytes");

                        EventMetadata eventInfo = new EventMetadata();
                        eventInfo.Add("ErrorMessage", "Validation of temporary downloaded file failed");
                        eventInfo.Add("file", filePath);
                        eventInfo.Add("intendedPurpose", intendedPurpose);
                        eventInfo.Add("Exception", ex.ToString());
                        this.Tracer.RelatedEvent(EventLevel.Warning, "Warning", eventInfo);

                        throw ex;
                    }
                }
            }
        }

        private RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.CallbackResult TrySavePackOrLooseObject(IEnumerable<string> objectShas, bool unpackObjects, GitEndPointResponseData responseData)
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

                this.WriteLooseObject(responseData.Stream, objectShaList[0], bufToCopyWith);
            }
            else if (responseData.ContentType == GitObjectContentType.BatchedLooseObjects)
            {
                // To reduce allocations, reuse the same buffer when writing objects in this batch
                byte[] bufToCopyWith = new byte[StreamUtil.DefaultCopyBufferSize];

                BatchedLooseObjectDeserializer deserializer = new BatchedLooseObjectDeserializer(
                    responseData.Stream,
                    (stream, sha) => this.WriteLooseObject(stream, sha, bufToCopyWith));
                deserializer.ProcessObjects();
            }
            else
            {
                GitProcess.Result result = this.TryAddPackFile(responseData.Stream, unpackObjects);
                if (result.HasErrors)
                {
                    return new RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.CallbackResult(new InvalidOperationException("Could not add pack file: " + result.Errors), shouldRetry: false);
                }
            }

            return new RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.CallbackResult(new GitObjectsHttpRequestor.GitObjectTaskResult(true));
        }

        private GitProcess.Result TryAddPackFile(Stream contents, bool unpackObjects)
        {
            Debug.Assert(contents != null, "contents should not be null");

            GitProcess.Result result;

            if (unpackObjects)
            {
                result = new GitProcess(this.Enlistment).UnpackObjects(contents);
            }
            else
            {
                string packfilePath = GetRandomPackName(this.Enlistment.GitPackRoot);
                using (FileStream fileStream = File.OpenWrite(packfilePath))
                {
                    StreamUtil.CopyToWithBuffer(contents, fileStream);
                }

                this.ValidateTempFile(packfilePath, packfilePath);

                result = new GitProcess(this.Enlistment).IndexPack(packfilePath);
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
    }
}
