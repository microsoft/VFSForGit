using System.Net;
using GVFS.Common.Http;
using GVFS.Tests.Should;
using NUnit.Framework;

namespace GVFS.UnitTests.Http
{
    [TestFixture]
    public class HttpRequestorTests
    {
        [TestCase]
        public void Unauthorized401RejectsCredentials()
        {
            HttpRequestor.ShouldRejectCredentials(HttpStatusCode.Unauthorized)
                .ShouldEqual(true, "A 401 is a definitive auth failure and must reject credentials");
        }

        [TestCase]
        public void Redirect302RejectsCredentials()
        {
            HttpRequestor.ShouldRejectCredentials(HttpStatusCode.Redirect)
                .ShouldEqual(true, "A 302 is the Azure DevOps sign-in redirect and must reject credentials");
        }

        [TestCase]
        public void BadRequest400DoesNotRejectCredentials()
        {
            // A 400 is a request/formatting problem, not an expired credential.
            // An expired or invalid credential always returns 401 or 302, never 400. Rejecting
            // credentials on 400 erased valid credentials and caused a credential-popup storm.
            HttpRequestor.ShouldRejectCredentials(HttpStatusCode.BadRequest)
                .ShouldEqual(false, "A 400 is not an auth failure and must NOT reject credentials");
        }

        [TestCase]
        public void CommonNonAuthStatusesDoNotRejectCredentials()
        {
            HttpRequestor.ShouldRejectCredentials(HttpStatusCode.NotFound)
                .ShouldEqual(false, "A 404 must NOT reject credentials");
            HttpRequestor.ShouldRejectCredentials(HttpStatusCode.InternalServerError)
                .ShouldEqual(false, "A 500 must NOT reject credentials");
            HttpRequestor.ShouldRejectCredentials(HttpStatusCode.RequestTimeout)
                .ShouldEqual(false, "A 408 must NOT reject credentials");
        }
    }
}
