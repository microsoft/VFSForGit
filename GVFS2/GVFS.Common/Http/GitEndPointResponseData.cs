using GVFS.Common.Git;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;

namespace GVFS.Common.Http
{
    public class GitEndPointResponseData : IDisposable
    {
        private HttpResponseMessage message;
        private Action onResponseDisposed;

        /// <summary>
        /// Constructor used when GitEndPointResponseData contains an error response
        /// </summary>
        public GitEndPointResponseData(HttpStatusCode statusCode, Exception error, bool shouldRetry, HttpResponseMessage message, Action onResponseDisposed)
        {
            this.StatusCode = statusCode;
            this.Error = error;
            this.ShouldRetry = shouldRetry;
            this.message = message;
            this.onResponseDisposed = onResponseDisposed;
        }

        /// <summary>
        /// Constructor used when GitEndPointResponseData contains a successful response
        /// </summary>
        public GitEndPointResponseData(HttpStatusCode statusCode, string contentType, Stream responseStream, HttpResponseMessage message, Action onResponseDisposed)
            : this(statusCode, null, false, message, onResponseDisposed)
        {
            this.Stream = responseStream;
            this.ContentType = MapContentType(contentType);
        }

        public Exception Error { get; }

        public bool ShouldRetry { get; }

        public HttpStatusCode StatusCode { get; }

        public Stream Stream { get; private set; }

        public bool HasErrors
        {
            get { return this.StatusCode != HttpStatusCode.OK; }
        }

        public GitObjectContentType ContentType { get; }

        /// <summary>
        /// Reads the underlying stream until it ends returning all content as a string.
        /// </summary>
        public string RetryableReadToEnd()
        {
            if (this.Stream == null)
            {
                throw new RetryableException("Stream is null (this could be a result of network flakiness), retrying.");
            }

            if (!this.Stream.CanRead)
            {
                throw new RetryableException("Stream is not readable (this could be a result of network flakiness), retrying.");
            }

            using (StreamReader contentStreamReader = new StreamReader(this.Stream))
            {
                try
                {
                    return contentStreamReader.ReadToEnd();
                }
                catch (Exception ex)
                {
                    // All exceptions potentially from network should be retried
                    throw new RetryableException("Exception while reading stream. See inner exception for details.", ex);
                }
            }
        }

        /// <summary>
        /// Reads the stream until it ends returning each line as a string.
        /// </summary>
        public List<string> RetryableReadAllLines()
        {
            using (StreamReader contentStreamReader = new StreamReader(this.Stream))
            {
                List<string> output = new List<string>();

                while (true)
                {
                    string line;
                    try
                    {
                        if (contentStreamReader.EndOfStream)
                        {
                            break;
                        }

                        line = contentStreamReader.ReadLine();
                    }
                    catch (Exception ex)
                    {
                        // All exceptions potentially from network should be retried
                        throw new RetryableException("Exception while reading stream. See inner exception for details.", ex);
                    }

                    output.Add(line);
                }

                return output;
            }
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (this.message != null)
                {
                    this.message.Dispose();
                    this.message = null;
                }

                if (this.Stream != null)
                {
                    this.Stream.Dispose();
                    this.Stream = null;
                }

                if (this.onResponseDisposed != null)
                {
                    this.onResponseDisposed();
                    this.onResponseDisposed = null;
                }
            }
        }

        /// <summary>
        /// Convert from a string-based Content-Type to <see cref="GitObjectsHttpRequestor.ContentType"/>
        /// </summary>
        private static GitObjectContentType MapContentType(string contentType)
        {
            switch (contentType)
            {
                case GVFSConstants.MediaTypes.LooseObjectMediaType:
                    return GitObjectContentType.LooseObject;
                case GVFSConstants.MediaTypes.CustomLooseObjectsMediaType:
                    return GitObjectContentType.BatchedLooseObjects;
                case GVFSConstants.MediaTypes.PackFileMediaType:
                    return GitObjectContentType.PackFile;
                default:
                    return GitObjectContentType.None;
            }
        }
    }
}
