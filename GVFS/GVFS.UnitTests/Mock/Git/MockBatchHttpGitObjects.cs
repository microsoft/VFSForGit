using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace GVFS.UnitTests.Mock.Git
{
    public class MockBatchHttpGitObjects : GitObjectsHttpRequestor
    {
        private Func<string, string> objectResolver;

        public MockBatchHttpGitObjects(ITracer tracer, Enlistment enlistment, Func<string, string> objectResolver)
            : base(tracer, enlistment, new MockCacheServerInfo(), new RetryConfig())
        {
            this.objectResolver = objectResolver;
        }

        public override List<GitObjectSize> QueryForFileSizes(IEnumerable<string> objectIds, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
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
            return this.StreamObjects(objectIds, onSuccess, onFailure);
        }

        private RetryWrapper<GitObjectTaskResult>.InvocationResult StreamObjects(
            IEnumerable<string> objectIds,
            Func<int, GitEndPointResponseData, RetryWrapper<GitObjectTaskResult>.CallbackResult> onSuccess,
            Action<RetryWrapper<GitObjectTaskResult>.ErrorEventArgs> onFailure)
        {
            for (int i = 0; i < this.RetryConfig.MaxAttempts; ++i)
            {
                try
                {
                    using (ReusableMemoryStream mem = new ReusableMemoryStream(string.Empty))
                    using (BinaryWriter writer = new BinaryWriter(mem))
                    {
                        writer.Write(new byte[] { (byte)'G', (byte)'V', (byte)'F', (byte)'S', (byte)' ', 1 });

                        foreach (string objectId in objectIds)
                        {
                            string contents = this.objectResolver(objectId);
                            if (!string.IsNullOrEmpty(contents))
                            {
                                writer.Write(this.SHA1BytesFromString(objectId));
                                byte[] bytes = Encoding.UTF8.GetBytes(contents);
                                writer.Write((long)bytes.Length);
                                writer.Write(bytes);
                            }
                            else
                            {
                                writer.Write(new byte[20]);
                                writer.Write(0L);
                            }
                        }

                        writer.Write(new byte[20]);
                        writer.Flush();
                        mem.Seek(0, SeekOrigin.Begin);

                        using (GitEndPointResponseData response = new GitEndPointResponseData(
                            HttpStatusCode.OK,
                            GVFSConstants.MediaTypes.CustomLooseObjectsMediaType,
                            mem,
                            message: null,
                            onResponseDisposed: null))
                        {
                            RetryWrapper<GitObjectTaskResult>.CallbackResult result = onSuccess(1, response);
                            return new RetryWrapper<GitObjectTaskResult>.InvocationResult(1, true, result.Result);
                        }
                    }
                }
                catch
                {
                    continue;
                }
            }

            return new RetryWrapper<GitObjectTaskResult>.InvocationResult(this.RetryConfig.MaxAttempts, null);
        }

        private byte[] SHA1BytesFromString(string s)
        {
            s.Length.ShouldEqual(40);

            byte[] output = new byte[20];
            for (int x = 0; x < s.Length; x += 2)
            {
                output[x / 2] = Convert.ToByte(s.Substring(x, 2), 16);
            }

            return output;
        }
    }
}
