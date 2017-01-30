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
        protected readonly HttpGitObjects GitObjectRequestor;

        private const string AreaPath = "GitObjects";

        public GitObjects(ITracer tracer, Enlistment enlistment, HttpGitObjects httpGitObjects)
        {
            this.Tracer = tracer;
            this.Enlistment = enlistment;
            this.GitObjectRequestor = httpGitObjects;
        }

        public enum DownloadAndSaveObjectResult
        {
            Success,
            ObjectNotOnServer,
            Error
        }

        public virtual bool TryDownloadAndSaveCommits(IEnumerable<string> commitShas, int commitDepth)
        {
            return this.TryDownloadAndSaveObjects(commitShas, commitDepth, preferLooseObjects: false);
        }

        public bool TryDownloadAndSaveBlobs(IEnumerable<string> blobShas)
        {
            return this.TryDownloadAndSaveObjects(blobShas, commitDepth: 1, preferLooseObjects: true);
        }

        public void DownloadPrefetchPacks(long latestTimestamp)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("latestTimestamp", latestTimestamp);

            using (ITracer activity = this.Tracer.StartActivity(nameof(this.DownloadPrefetchPacks), EventLevel.Informational, metadata))
            {
                RetryWrapper<HttpGitObjects.GitObjectTaskResult>.InvocationResult result = this.GitObjectRequestor.TrySendProtocolRequest(
                    onSuccess: (tryCount, response) => this.DeserializePrefetchPacks(response, ref latestTimestamp),
                    onFailure: RetryWrapper<HttpGitObjects.GitObjectTaskResult>.StandardErrorHandler(activity, nameof(this.DownloadPrefetchPacks)),
                    method: HttpMethod.Get,
                    endPointGenerator: () => new Uri(
                        string.Format(
                            "{0}?lastPackTimestamp={1}",
                            this.Enlistment.PrefetchEndpointUrl,
                            latestTimestamp)),
                    requestBodyGenerator: () => null,
                    acceptType: new MediaTypeWithQualityHeaderValue(GVFSConstants.MediaTypes.PrefetchPackFilesAndIndexesMediaType));

                if (!result.Succeeded)
                {
                    if (result.Result != null && result.Result.HttpStatusCodeResult == HttpStatusCode.NotFound)
                    {
                        EventMetadata warning = new EventMetadata();
                        warning.Add("ErrorMessage", "The server does not support /gvfs/prefetch.");
                        warning.Add(nameof(this.Enlistment.PrefetchEndpointUrl), this.Enlistment.PrefetchEndpointUrl);
                        activity.RelatedEvent(EventLevel.Warning, "CommandNotSupported", warning);
                    }
                    else
                    {
                        EventMetadata error = new EventMetadata();
                        error.Add("latestTimestamp", latestTimestamp);
                        error.Add("Exception", result.Error);
                        error.Add("ErrorMessage", "DownloadPrefetchPacks failed.");
                        error.Add(nameof(this.Enlistment.PrefetchEndpointUrl), this.Enlistment.PrefetchEndpointUrl);
                        activity.RelatedError(error);
                    }
                }
            }
        }

        public virtual string WriteLooseObject(string repoRoot, Stream responseStream, string sha, byte[] bufToCopyWith = null)
        {
            LooseObjectToWrite toWrite = GetLooseObjectDestination(repoRoot, sha);

            using (Stream fileStream = OpenTempLooseObjectStream(toWrite.TempFile, async: false))
            {
                if (bufToCopyWith != null)
                {
                    StreamUtil.CopyToWithBuffer(responseStream, fileStream, bufToCopyWith);
                }
                else
                {
                    responseStream.CopyTo(fileStream);
                }
            }

            this.FinalizeTempFile(sha, toWrite);

            return toWrite.ActualFile;
        }

        public virtual string WriteTempPackFile(HttpGitObjects.GitEndPointResponseData response)
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
                    source.CopyTo(fileStream);
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

        protected virtual DownloadAndSaveObjectResult TryDownloadAndSaveObject(string objectSha)
        {
            if (objectSha == GVFSConstants.AllZeroSha)
            {
                return DownloadAndSaveObjectResult.Error;
            }

            RetryWrapper<HttpGitObjects.GitObjectTaskResult>.InvocationResult output = this.GitObjectRequestor.TryDownloadLooseObject(
                objectSha,
                onSuccess: (tryCount, response) =>
                {
                    this.WriteLooseObject(this.Enlistment.WorkingDirectoryRoot, response.Stream, objectSha);
                    return new RetryWrapper<HttpGitObjects.GitObjectTaskResult>.CallbackResult(new HttpGitObjects.GitObjectTaskResult(true));
                },
                onFailure: this.HandleDownloadAndSaveObjectError);

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

        private static LooseObjectToWrite GetLooseObjectDestination(string repoRoot, string sha)
        {
            string firstTwoDigits = sha.Substring(0, 2);
            string remainingDigits = sha.Substring(2);
            string twoLetterFolderName = Path.Combine(repoRoot, GVFSConstants.DotGit.Objects.Root, firstTwoDigits);
            Directory.CreateDirectory(twoLetterFolderName);

            return new LooseObjectToWrite(
                tempFile: Path.Combine(twoLetterFolderName, Path.GetRandomFileName()),
                actualFile: Path.Combine(twoLetterFolderName, remainingDigits));
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

        private bool TryDownloadAndSaveObjects(IEnumerable<string> objectIds, int commitDepth, bool preferLooseObjects)
        {
            RetryWrapper<HttpGitObjects.GitObjectTaskResult>.InvocationResult output = this.GitObjectRequestor.TryDownloadObjects(
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

        private void HandleDownloadAndSaveObjectError(RetryWrapper<HttpGitObjects.GitObjectTaskResult>.ErrorEventArgs errorArgs)
        {
            // Silence logging 404's for object downloads. They are far more likely to be git checking for the 
            // previous existence of a new object than a truly missing object.
            HttpGitObjects.HttpGitObjectsException ex = errorArgs.Error as HttpGitObjects.HttpGitObjectsException;
            if (ex != null && ex.StatusCode == HttpStatusCode.NotFound)
            {
                return;
            }

            RetryWrapper<HttpGitObjects.GitObjectTaskResult>.StandardErrorHandler(this.Tracer, nameof(this.TryDownloadAndSaveObject))(errorArgs);
        }

        /// <summary>
        /// Uses a <see cref="PrefetchPacksDeserializer"/> to read the packs from the stream.
        /// </summary>
        private RetryWrapper<HttpGitObjects.GitObjectTaskResult>.CallbackResult DeserializePrefetchPacks(
           HttpGitObjects.GitEndPointResponseData response, ref long latestTimestamp)
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
                        return new RetryWrapper<HttpGitObjects.GitObjectTaskResult>.CallbackResult(null, true);
                    }

                    // We will try to build an index if the server does not send one
                    if (pack.IndexStream == null)
                    {
                        if (!this.TryBuildIndex(activity, pack, packFullPath))
                        {
                            return new RetryWrapper<HttpGitObjects.GitObjectTaskResult>.CallbackResult(null, true);
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
                        return new RetryWrapper<HttpGitObjects.GitObjectTaskResult>.CallbackResult(null, true);
                    }

                    latestTimestamp = pack.Timestamp;
                }

                return new RetryWrapper<HttpGitObjects.GitObjectTaskResult>.CallbackResult(
                    new HttpGitObjects.GitObjectTaskResult(true));
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

                    File.Move(toWrite.TempFile, toWrite.ActualFile);
                }
            }
            catch (IOException)
            {
                // IOExceptions happen when someone else is writing to our object. 
                // That implies they are doing what we're doing, which should be a success
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
                    int bytesRead = fs.Read(buffer, 0, buffer.Length);
                    if (buffer.Take(bytesRead).All(b => b == 0))
                    {
                        throw new RetryableException("Temp file for '" + intendedPurpose + "' was written with " + buffer.Length + " null bytes");
                    }
                }
            }
        }

        private RetryWrapper<HttpGitObjects.GitObjectTaskResult>.CallbackResult TrySavePackOrLooseObject(IEnumerable<string> objectShas, bool unpackObjects, HttpGitObjects.GitEndPointResponseData responseData)
        {
            if (responseData.ContentType == HttpGitObjects.ContentType.LooseObject)
            {
                List<string> objectShaList = objectShas.Distinct().ToList();
                if (objectShaList.Count != 1)
                {
                    return new RetryWrapper<HttpGitObjects.GitObjectTaskResult>.CallbackResult(new InvalidOperationException("Received loose object when multiple objects were requested."), shouldRetry: false);
                }

                this.WriteLooseObject(this.Enlistment.WorkingDirectoryRoot, responseData.Stream, objectShaList[0]);
            }
            else if (responseData.ContentType == HttpGitObjects.ContentType.BatchedLooseObjects)
            {
                BatchedLooseObjectDeserializer deserializer = new BatchedLooseObjectDeserializer(
                    responseData.Stream, 
                    (stream, sha) => this.WriteLooseObject(this.Enlistment.WorkingDirectoryRoot, stream, sha));
                deserializer.ProcessObjects();
            }
            else
            {
                GitProcess.Result result = this.TryAddPackFile(responseData.Stream, unpackObjects);
                if (result.HasErrors)
                {
                    return new RetryWrapper<HttpGitObjects.GitObjectTaskResult>.CallbackResult(new InvalidOperationException("Could not add pack file: " + result.Errors), shouldRetry: false);
                }
            }

            return new RetryWrapper<HttpGitObjects.GitObjectTaskResult>.CallbackResult(new HttpGitObjects.GitObjectTaskResult(true));
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
                    contents.CopyTo(fileStream);
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
