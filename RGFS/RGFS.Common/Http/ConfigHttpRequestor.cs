using RGFS.Common.NetworkStreams;
using RGFS.Common.Tracing;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Net.Http;
using System.Threading;

namespace RGFS.Common.Http
{
    public class ConfigHttpRequestor : HttpRequestor
    {
        private readonly string repoUrl;
        
        public ConfigHttpRequestor(ITracer tracer, Enlistment enlistment, RetryConfig retryConfig) 
            : base(tracer, retryConfig, enlistment.Authentication)
        {
            this.repoUrl = enlistment.RepoUrl;
        }

        public bool TryQueryRGFSConfig(out RGFSConfig rgfsConfig)
        {
            rgfsConfig = null;

            Uri rgfsConfigEndpoint;
            string rgfsConfigEndpointString = this.repoUrl + RGFSConstants.Endpoints.RGFSConfig;
            try
            {
                rgfsConfigEndpoint = new Uri(rgfsConfigEndpointString);
            }
            catch (UriFormatException e)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Method", nameof(this.TryQueryRGFSConfig));
                metadata.Add("Exception", e.ToString());
                metadata.Add("Url", rgfsConfigEndpointString);
                this.Tracer.RelatedError(metadata, "UriFormatException when constructing Uri", Keywords.Network);

                return false;
            }

            CancellationToken neverCanceledToken = new CancellationToken(canceled: false);
            long requestId = HttpRequestor.GetNewRequestId();
            RetryWrapper<RGFSConfig> retrier = new RetryWrapper<RGFSConfig>(this.RetryConfig.MaxAttempts, neverCanceledToken);
            retrier.OnFailure += RetryWrapper<RGFSConfig>.StandardErrorHandler(this.Tracer, requestId, "QueryRgfsConfig");

            RetryWrapper<RGFSConfig>.InvocationResult output = retrier.Invoke(
                tryCount =>
                {
                    GitEndPointResponseData response = this.SendRequest(
                        requestId, 
                        rgfsConfigEndpoint, 
                        HttpMethod.Get, 
                        requestContent: null, 
                        cancellationToken: neverCanceledToken);

                    if (response.HasErrors)
                    {
                        return new RetryWrapper<RGFSConfig>.CallbackResult(response.Error, response.ShouldRetry);
                    }

                    try
                    {
                        using (StreamReader reader = new StreamReader(response.Stream))
                        {
                            string configString = reader.RetryableReadToEnd();
                            RGFSConfig config = JsonConvert.DeserializeObject<RGFSConfig>(configString);
                            return new RetryWrapper<RGFSConfig>.CallbackResult(config);
                        }
                    }
                    catch (JsonReaderException e)
                    {
                        return new RetryWrapper<RGFSConfig>.CallbackResult(e, false);
                    }
                });

            if (output.Succeeded)
            {
                rgfsConfig = output.Result;
                return true;
            }

            return false;
        }
    }
}
