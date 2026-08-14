using System;
using System.Net;
using System.Threading;
using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;

namespace GVFS.UnitTests.Http
{
    /// <summary>
    /// SKETCH (design proposal). Exercises the actual probe decision in
    /// <see cref="HttpRequestor.CredentialProbeConfirmsAuthFailure"/> via a test seam that overrides
    /// the network send, so the four decision paths (probe 401/302 = reject; probe 200/404 = keep;
    /// no probe URI = keep; transport failure = keep) and the single-flight memoization are covered.
    /// </summary>
    [TestFixture]
    public class CredentialProbeBehaviorTests
    {
        private static readonly Uri FailedRequestUri = new Uri("mock://repo/gvfs/objects/badsha");

        [TestCase]
        public void Probe401ConfirmsAuthFailure()
        {
            ProbeTestableHttpGitObjects dut = new ProbeTestableHttpGitObjects(ProbeUri(), HttpStatusCode.Unauthorized);
            dut.CredentialProbeConfirmsAuthFailure(1, FailedRequestUri, "authString", CancellationToken.None)
                .ShouldEqual(true, "A probe that returns 401 confirms the credential is bad");
            dut.ProbeCallCount.ShouldEqual(1);
        }

        [TestCase]
        public void Probe302ConfirmsAuthFailure()
        {
            ProbeTestableHttpGitObjects dut = new ProbeTestableHttpGitObjects(ProbeUri(), HttpStatusCode.Redirect);
            dut.CredentialProbeConfirmsAuthFailure(1, FailedRequestUri, "authString", CancellationToken.None)
                .ShouldEqual(true, "A probe that returns 302 (sign-in redirect) confirms the credential is bad");
        }

        [TestCase]
        public void Probe200KeepsCredential()
        {
            ProbeTestableHttpGitObjects dut = new ProbeTestableHttpGitObjects(ProbeUri(), HttpStatusCode.OK);
            dut.CredentialProbeConfirmsAuthFailure(1, FailedRequestUri, "authString", CancellationToken.None)
                .ShouldEqual(false, "A probe that returns 200 proves the credential is valid - keep it");
        }

        [TestCase]
        public void Probe404KeepsCredential()
        {
            // A 404 proves auth passed: we reached "object not found" past the auth gate.
            ProbeTestableHttpGitObjects dut = new ProbeTestableHttpGitObjects(ProbeUri(), HttpStatusCode.NotFound);
            dut.CredentialProbeConfirmsAuthFailure(1, FailedRequestUri, "authString", CancellationToken.None)
                .ShouldEqual(false, "A probe that returns 404 proves auth passed - keep the credential");
        }

        [TestCase]
        public void TransportFailureKeepsCredential()
        {
            // probeStatus null => TryProbeCredential returns false (inconclusive).
            ProbeTestableHttpGitObjects dut = new ProbeTestableHttpGitObjects(ProbeUri(), probeStatus: null);
            dut.CredentialProbeConfirmsAuthFailure(1, FailedRequestUri, "authString", CancellationToken.None)
                .ShouldEqual(false, "An inconclusive probe (transport failure) must NOT reject the credential");
        }

        [TestCase]
        public void NoProbeUriKeepsCredentialWithoutProbing()
        {
            ProbeTestableHttpGitObjects dut = new ProbeTestableHttpGitObjects(probeUri: null, probeStatus: HttpStatusCode.Unauthorized);
            dut.CredentialProbeConfirmsAuthFailure(1, FailedRequestUri, "authString", CancellationToken.None)
                .ShouldEqual(false, "With no probe URI the credential must be kept (conservative)");
            dut.ProbeCallCount.ShouldEqual(0, "No probe should be sent when there is no probe URI");
        }

        [TestCase]
        public void RepeatedProbesForSameCredentialAreSingleFlighted()
        {
            ProbeTestableHttpGitObjects dut = new ProbeTestableHttpGitObjects(ProbeUri(), HttpStatusCode.NotFound);

            bool first = dut.CredentialProbeConfirmsAuthFailure(1, FailedRequestUri, "sameAuth", CancellationToken.None);
            bool second = dut.CredentialProbeConfirmsAuthFailure(2, FailedRequestUri, "sameAuth", CancellationToken.None);

            first.ShouldEqual(false);
            second.ShouldEqual(false);
            dut.ProbeCallCount.ShouldEqual(1, "The second 400 for the same credential should reuse the memoized probe result");
        }

        [TestCase]
        public void DifferentCredentialTriggersFreshProbe()
        {
            ProbeTestableHttpGitObjects dut = new ProbeTestableHttpGitObjects(ProbeUri(), HttpStatusCode.NotFound);

            dut.CredentialProbeConfirmsAuthFailure(1, FailedRequestUri, "authOne", CancellationToken.None);
            dut.CredentialProbeConfirmsAuthFailure(2, FailedRequestUri, "authTwo", CancellationToken.None);

            dut.ProbeCallCount.ShouldEqual(2, "A different credential must not reuse the previous credential's probe result");
        }

        private static Uri ProbeUri()
        {
            return new Uri("mock://cache/gvfs/objects/4b825dc642cb6eb9a060e54bf8d69288fbee4904");
        }

        private sealed class ProbeTestableHttpGitObjects : GitObjectsHttpRequestor
        {
            private readonly Uri probeUri;
            private readonly HttpStatusCode? probeStatus;

            public ProbeTestableHttpGitObjects(Uri probeUri, HttpStatusCode? probeStatus)
                : base(new MockTracer(), new MockGVFSEnlistment(), new MockCacheServerInfo(), new RetryConfig(maxRetries: 1))
            {
                this.probeUri = probeUri;
                this.probeStatus = probeStatus;
            }

            public int ProbeCallCount { get; private set; }

            protected override Uri GetCredentialProbeUri(Uri failedRequestUri)
            {
                return this.probeUri;
            }

            protected override bool TryProbeCredential(Uri probeUri, string authString, CancellationToken cancellationToken, out HttpStatusCode probeStatus)
            {
                this.ProbeCallCount++;
                if (this.probeStatus.HasValue)
                {
                    probeStatus = this.probeStatus.Value;
                    return true;
                }

                probeStatus = default(HttpStatusCode);
                return false;
            }
        }
    }
}
