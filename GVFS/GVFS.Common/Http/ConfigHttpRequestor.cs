using GVFS.Common.Tracing;
using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Threading;

namespace GVFS.Common.Http
{
    public class ConfigHttpRequestor : HttpRequestor
    {
        private readonly string repoUrl;
        
        public ConfigHttpRequestor(ITracer tracer, Enlistment enlistment, RetryConfig retryConfig) 
            : base(tracer, retryConfig, enlistment.Authentication)
        {
            this.repoUrl = enlistment.RepoUrl;
        }

        public bool TryQueryGVFSConfig(out GVFSConfig gvfsConfig)
        {
            gvfsConfig = null;

            Uri gvfsConfigEndpoint;
            string gvfsConfigEndpointString = this.repoUrl + GVFSConstants.Endpoints.GVFSConfig;
            try
            {
                gvfsConfigEndpoint = new Uri(gvfsConfigEndpointString);
            }
            catch (UriFormatException e)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Method", nameof(this.TryQueryGVFSConfig));
                metadata.Add("Exception", e.ToString());
                metadata.Add("Url", gvfsConfigEndpointString);
                this.Tracer.RelatedError(metadata, "UriFormatException when constructing Uri", Keywords.Network);

                return false;
            }

            long requestId = HttpRequestor.GetNewRequestId();
            RetryWrapper<GVFSConfig> retrier = new RetryWrapper<GVFSConfig>(this.RetryConfig.MaxAttempts, CancellationToken.None);
            retrier.OnFailure += RetryWrapper<GVFSConfig>.StandardErrorHandler(this.Tracer, requestId, "QueryGvfsConfig");

            RetryWrapper<GVFSConfig>.InvocationResult output = retrier.Invoke(
                tryCount =>
                {
                    using (GitEndPointResponseData response = this.SendRequest(
                        requestId,
                        gvfsConfigEndpoint,
                        HttpMethod.Get,
                        requestContent: null,
                        cancellationToken: CancellationToken.None))
                    {
                        if (response.HasErrors)
                        {
                            return new RetryWrapper<GVFSConfig>.CallbackResult(response.Error, response.ShouldRetry);
                        }

                        try
                        {
                            string configString = response.RetryableReadToEnd();
                            GVFSConfig config = JsonConvert.DeserializeObject<GVFSConfig>(configString);
                            return new RetryWrapper<GVFSConfig>.CallbackResult(config);
                        }
                        catch (JsonReaderException e)
                        {
                            return new RetryWrapper<GVFSConfig>.CallbackResult(e, shouldRetry: false);
                        }
                    }
                });

            if (output.Succeeded)
            {
                gvfsConfig = output.Result;
                return true;
            }

            return false;
        }
    }
}
