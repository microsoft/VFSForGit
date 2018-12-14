using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;

namespace GVFS.UnitTests.Mock.Git
{
    public class MockHttpGitObjects : GitObjectsHttpRequestor
    {
        private Dictionary<string, long> shaLengths = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> shaContents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public MockHttpGitObjects(ITracer tracer, Enlistment enlistment)
            : base(tracer, enlistment, new MockCacheServerInfo(), new RetryConfig())
        {
        }

        public void AddShaLength(string sha, long length)
        {
            this.shaLengths.Add(sha, length);
        }

        public void AddBlobContent(string sha, string content)
        {
            this.shaContents.Add(sha, content);
        }

        public void AddShaLengths(IEnumerable<KeyValuePair<string, long>> shaLengthPairs)
        {
            foreach (KeyValuePair<string, long> kvp in shaLengthPairs)
            {
                this.AddShaLength(kvp.Key, kvp.Value);
            }
        }

        public override List<GitObjectSize> QueryForFileSizes(IEnumerable<string> objectIds, CancellationToken cancellationToken)
        {
            return objectIds.Select(oid => new GitObjectSize(oid, this.QueryForFileSize(oid))).ToList();
        }

        public override GitRefs QueryInfoRefs(string branch)
        {
            throw new NotImplementedException();
        }

        public override RetryWrapper<GitObjectTaskResult>.InvocationResult TryDownloadObjects(
            Func<IEnumerable<string>> objectIdGenerator,
            Func<int, GitEndPointResponseData, RetryWrapper<GitObjectTaskResult>.CallbackResult> onSuccess,
            Action<RetryWrapper<GitObjectTaskResult>.ErrorEventArgs> onFailure,
            bool preferBatchedLooseObjects)
        {
            return this.TryDownloadObjects(objectIdGenerator(), onSuccess, onFailure, preferBatchedLooseObjects);
        }

        public override RetryWrapper<GitObjectTaskResult>.InvocationResult TryDownloadObjects(
            IEnumerable<string> objectIds,
            Func<int, GitEndPointResponseData, RetryWrapper<GitObjectTaskResult>.CallbackResult> onSuccess,
            Action<RetryWrapper<GitObjectTaskResult>.ErrorEventArgs> onFailure,
            bool preferBatchedLooseObjects)
        {
            // When working within the mocks, we do not support multiple objects.
            // PhysicalGitObjects should be overridden to serialize the calls.
            objectIds.Count().ShouldEqual(1);
            string objectId = objectIds.First();
            return this.GetSingleObject(objectId, onSuccess, onFailure);
        }

        private RetryWrapper<GitObjectTaskResult>.InvocationResult GetSingleObject(
            string objectId,
            Func<int, GitEndPointResponseData, RetryWrapper<GitObjectTaskResult>.CallbackResult> onSuccess,
            Action<RetryWrapper<GitObjectTaskResult>.ErrorEventArgs> onFailure)
        {
            if (this.shaContents.ContainsKey(objectId))
            {
                using (GitEndPointResponseData response = new GitEndPointResponseData(
                    HttpStatusCode.OK,
                    GVFSConstants.MediaTypes.LooseObjectMediaType,
                    new ReusableMemoryStream(this.shaContents[objectId]),
                    message: null,
                    onResponseDisposed: null))
                {
                    RetryWrapper<GitObjectTaskResult>.CallbackResult result = onSuccess(1, response);
                    return new RetryWrapper<GitObjectTaskResult>.InvocationResult(1, true, result.Result);
                }
            }

            if (onFailure != null)
            {
                onFailure(new RetryWrapper<GitObjectTaskResult>.ErrorEventArgs(new Exception("Could not find mock object: " + objectId), 1, false));
            }

            return new RetryWrapper<GitObjectTaskResult>.InvocationResult(1, new Exception("Mock failure in TryDownloadObjectsAsync"));
        }

        private long QueryForFileSize(string objectId)
        {
            this.shaLengths.ContainsKey(objectId).ShouldEqual(true);
            return this.shaLengths[objectId];
        }
    }
}
