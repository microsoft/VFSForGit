using GVFS.Common.Git;
using System;
using System.IO;
using System.Net;

namespace GVFS.Common.Http
{
    public class GitEndPointResponseData
    {
        /// <summary>
        /// Constructor used when GitEndPointResponseData contains an error response
        /// </summary>
        public GitEndPointResponseData(HttpStatusCode statusCode, Exception error, bool shouldRetry)
        {
            this.StatusCode = statusCode;
            this.Error = error;
            this.ShouldRetry = shouldRetry;
        }

        /// <summary>
        /// Constructor used when GitEndPointResponseData contains a successful response
        /// </summary>
        public GitEndPointResponseData(HttpStatusCode statusCode, string contentType, Stream responseStream)
            : this(statusCode, null, false)
        {
            this.Stream = responseStream;
            this.ContentType = MapContentType(contentType);
        }

        /// <summary>
        /// Stream returned by a successful response.  If the response is an error, Stream will be null
        /// </summary>
        public Stream Stream { get; }

        public Exception Error { get; }

        public bool ShouldRetry { get; }

        public HttpStatusCode StatusCode { get; }

        public bool HasErrors
        {
            get { return this.StatusCode != HttpStatusCode.OK; }
        }

        public GitObjectContentType ContentType { get; }

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
