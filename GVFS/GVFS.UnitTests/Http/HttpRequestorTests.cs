using GVFS.Common.Http;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Net;

namespace GVFS.UnitTests.Http
{
    [TestFixture]
    public class HttpRequestorTests
    {
        [TestCase]
        public void Unauthorized401RejectsCredentials()
        {
            HttpRequestor.ShouldRejectCredentials(HttpStatusCode.Unauthorized, responseBody: null)
                .ShouldEqual(true, "A 401 is a definitive auth failure and must reject credentials");
        }

        [TestCase]
        public void Redirect302RejectsCredentials()
        {
            HttpRequestor.ShouldRejectCredentials(HttpStatusCode.Redirect, responseBody: null)
                .ShouldEqual(true, "A 302 is the Azure DevOps sign-in redirect and must reject credentials");
        }

        [TestCase]
        public void BadRequest400WithAuthRequiredMessageRejectsCredentials()
        {
            // The GVFS cache server returns a 400 (instead of a 401) when the request carried
            // no parseable Basic Authorization header. That single 400 genuinely means
            // "authentication required", so it must reject credentials. We recognize it by the
            // cache server's response body.
            HttpRequestor.ShouldRejectCredentials(
                    HttpStatusCode.BadRequest,
                    HttpRequestor.CacheServerAuthRequiredBadRequestMessage)
                .ShouldEqual(true, "A 400 whose body is the cache server's auth-required message must reject credentials");
        }

        [TestCase]
        public void BadRequest400AuthRequiredMessageMatchIsCaseInsensitiveAndSubstring()
        {
            // The match is a case-insensitive substring so it stays robust if the server
            // wraps or prefixes the text.
            HttpRequestor.ShouldRejectCredentials(
                    HttpStatusCode.BadRequest,
                    "Error: a valid basic authorization header is required. (request 123)")
                .ShouldEqual(true, "The auth-required message match must be a case-insensitive substring");
        }

        [TestCase]
        public void BadRequest400WithNonAuthBodyDoesNotRejectCredentials()
        {
            // The storm case: a corrupt placeholder SHA makes the cache server return a 400
            // with an "Invalid ObjectId" body. That is NOT an auth failure and must not erase
            // a valid credential.
            HttpRequestor.ShouldRejectCredentials(
                    HttpStatusCode.BadRequest,
                    "Error processing GVFS request: Invalid ObjectId in the URI.")
                .ShouldEqual(false, "A non-auth 400 (e.g. invalid object id) must NOT reject credentials");
        }

        [TestCase]
        public void BadRequest400WithNullBodyDoesNotRejectCredentials()
        {
            HttpRequestor.ShouldRejectCredentials(HttpStatusCode.BadRequest, responseBody: null)
                .ShouldEqual(false, "A 400 with no body must NOT reject credentials");
        }

        [TestCase]
        public void CommonNonAuthStatusesDoNotRejectCredentials()
        {
            HttpRequestor.ShouldRejectCredentials(HttpStatusCode.NotFound, responseBody: null)
                .ShouldEqual(false, "A 404 must NOT reject credentials");
            HttpRequestor.ShouldRejectCredentials(HttpStatusCode.InternalServerError, responseBody: null)
                .ShouldEqual(false, "A 500 must NOT reject credentials");
            HttpRequestor.ShouldRejectCredentials(HttpStatusCode.RequestTimeout, responseBody: null)
                .ShouldEqual(false, "A 408 must NOT reject credentials");
        }

        [TestCase]
        public void AuthorityForTelemetryExcludesCredentialsAndRequestPath()
        {
            Uri uri = new Uri("https://alice:secret@cache.example.com:8443/private/path?token=sensitive#fragment");

            HttpRequestor.GetAuthorityForTelemetry(uri).ShouldEqual("cache.example.com:8443");
        }
    }
}
