using System;
using System.Net;

namespace RGFS.Common.Http
{
    public class GitObjectsHttpException : Exception
    {
        public GitObjectsHttpException(HttpStatusCode statusCode, string ex) : base(ex)
        {
            this.StatusCode = statusCode;
        }

        public HttpStatusCode StatusCode { get; }
    }
}
