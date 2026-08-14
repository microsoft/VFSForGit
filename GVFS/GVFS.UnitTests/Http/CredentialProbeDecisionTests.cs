using System.Net;
using GVFS.Common.Http;
using GVFS.Tests.Should;
using NUnit.Framework;

namespace GVFS.UnitTests.Http
{
    /// <summary>
    /// SKETCH (design proposal). Covers the decisive-signal rule the credential probe relies
    /// on: after a 400, we re-send the same credential to a known-good endpoint and reject the
    /// credential ONLY when that probe itself fails authentication (401/302). Any other probe
    /// status - including 200 and 404 - proves the credential got past auth and must be kept.
    /// </summary>
    [TestFixture]
    public class CredentialProbeDecisionTests
    {
        [TestCase]
        public void Probe401MeansRejectCredential()
        {
            HttpRequestor.ShouldRejectCredentials(HttpStatusCode.Unauthorized)
                .ShouldEqual(true, "A 401 probe response is a real auth failure - reject the credential");
        }

        [TestCase]
        public void Probe302MeansRejectCredential()
        {
            HttpRequestor.ShouldRejectCredentials(HttpStatusCode.Redirect)
                .ShouldEqual(true, "A 302 probe response is the sign-in redirect - reject the credential");
        }

        [TestCase]
        public void Probe200MeansKeepCredential()
        {
            HttpRequestor.ShouldRejectCredentials(HttpStatusCode.OK)
                .ShouldEqual(false, "A 200 probe response proves the credential is valid - keep it");
        }

        [TestCase]
        public void Probe404MeansKeepCredential()
        {
            // A 404 proves auth passed: we reached "object not found" past the auth gate.
            HttpRequestor.ShouldRejectCredentials(HttpStatusCode.NotFound)
                .ShouldEqual(false, "A 404 probe response proves auth passed - keep the credential");
        }

        [TestCase]
        public void Probe400MeansKeepCredential()
        {
            HttpRequestor.ShouldRejectCredentials(HttpStatusCode.BadRequest)
                .ShouldEqual(false, "Even a 400 probe response is not an auth failure - keep the credential");
        }
    }
}
