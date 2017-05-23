using GVFS.Common.Physical.FileSystem;
using GVFS.Common.Tracing;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Net.Http;

namespace GVFS.Common.Http
{
    public class ConfigHttpRequestor : HttpRequestor
    {
        private readonly string repoUrl;

        public ConfigHttpRequestor(ITracer tracer, Enlistment enlistment) 
            : base(tracer, enlistment.Authentication, maxConnections: 1)
        {
            this.repoUrl = enlistment.RepoUrl;
            this.MaxRetries = HttpRequestor.DefaultMaxRetries;
        }

        public int MaxRetries { get; set; }

        public GVFSConfig QueryGVFSConfig()
        {
            Uri gvfsConfigEndpoint;
            string gvfsConfigEndpointString = this.repoUrl + GVFSConstants.GVFSConfigEndpointSuffix;
            try
            {
                gvfsConfigEndpoint = new Uri(gvfsConfigEndpointString);
            }
            catch (UriFormatException e)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Method", nameof(this.QueryGVFSConfig));
                metadata.Add("ErrorMessage", e);
                metadata.Add("Url", gvfsConfigEndpointString);
                this.Tracer.RelatedError(metadata, Keywords.Network);

                return null;
            }

            RetryWrapper<GVFSConfig> retrier = new RetryWrapper<GVFSConfig>(this.MaxRetries);
            retrier.OnFailure += RetryWrapper<GVFSConfig>.StandardErrorHandler(this.Tracer, "QueryGvfsConfig");

            RetryWrapper<GVFSConfig>.InvocationResult output = retrier.Invoke(
                tryCount =>
                {
                    GitEndPointResponseData response = this.SendRequest(gvfsConfigEndpoint, HttpMethod.Get, null);
                    if (response.HasErrors)
                    {
                        return new RetryWrapper<GVFSConfig>.CallbackResult(response.Error, response.ShouldRetry);
                    }

                    try
                    {
                        using (StreamReader reader = new StreamReader(response.Stream))
                        {
                            string configString = reader.RetryableReadToEnd();
                            return new RetryWrapper<GVFSConfig>.CallbackResult(
                                JsonConvert.DeserializeObject<GVFSConfig>(configString));
                        }
                    }
                    catch (JsonReaderException e)
                    {
                        return new RetryWrapper<GVFSConfig>.CallbackResult(e, false);
                    }
                });

            return output.Result;
        }
    }
}
