using GVFS.Common;
using GVFS.Common.Http;
using System;
using System.Net;

namespace GVFS.UnitTests.Mock.Http
{
    /// <summary>
    /// Drives <see cref="GVFS.Common.Git.GitAuthentication.TryInitializeAndQueryGVFSConfig"/>
    /// with a fixed /gvfs/config probe outcome so the anonymous / authenticated /
    /// indeterminate branches can be tested without a server.
    /// </summary>
    internal class MockGVFSConfigRequestor : IGVFSConfigRequestor
    {
        private readonly bool succeedAnonymously;
        private readonly bool succeedAfterAuthentication;
        private readonly HttpStatusCode? statusCode;

        private MockGVFSConfigRequestor(bool succeedAnonymously, HttpStatusCode? statusCode, bool succeedAfterAuthentication = false)
        {
            this.succeedAnonymously = succeedAnonymously;
            this.statusCode = statusCode;
            this.succeedAfterAuthentication = succeedAfterAuthentication;
        }

        /// <summary>
        /// Number of times the probe was issued.
        /// </summary>
        public int QueryCount { get; private set; }

        /// <summary>
        /// The value of <paramref name="forceAnonymous"/> on the most recent probe.
        /// </summary>
        public bool LastQueryForcedAnonymous { get; private set; }

        public Action<bool> BeforeQueryResult { get; set; }

        /// <summary>
        /// The server allows anonymous access: the unauthenticated probe returns the config.
        /// </summary>
        public static MockGVFSConfigRequestor AnonymousSucceeds()
        {
            return new MockGVFSConfigRequestor(succeedAnonymously: true, statusCode: HttpStatusCode.OK);
        }

        /// <summary>
        /// The server requires authentication: the unauthenticated probe returns 401.
        /// </summary>
        public static MockGVFSConfigRequestor RequiresAuthentication()
        {
            return new MockGVFSConfigRequestor(succeedAnonymously: false, statusCode: HttpStatusCode.Unauthorized);
        }

        /// <summary>
        /// The initial unauthenticated query is rejected, then the authenticated retry succeeds.
        /// </summary>
        public static MockGVFSConfigRequestor RequiresAuthenticationThenSucceeds()
        {
            return new MockGVFSConfigRequestor(
                succeedAnonymously: false,
                statusCode: HttpStatusCode.Unauthorized,
                succeedAfterAuthentication: true);
        }

        /// <summary>
        /// The probe failed for a reason that says nothing about authentication, so
        /// whether the server allows anonymous access is unknown. Pass null for
        /// <paramref name="statusCode"/> to model a failure with no HTTP response at all.
        /// </summary>
        public static MockGVFSConfigRequestor Indeterminate(HttpStatusCode? statusCode)
        {
            return new MockGVFSConfigRequestor(succeedAnonymously: false, statusCode: statusCode);
        }

        public bool TryQueryGVFSConfig(bool logErrors, out ServerGVFSConfig serverGVFSConfig, out HttpStatusCode? httpStatus, out string errorMessage, bool forceAnonymous = false)
        {
            this.QueryCount++;
            this.LastQueryForcedAnonymous = forceAnonymous;
            this.BeforeQueryResult?.Invoke(forceAnonymous);

            bool querySucceeded = this.succeedAnonymously ||
                (this.succeedAfterAuthentication && this.QueryCount > 1 && !forceAnonymous);
            httpStatus = querySucceeded ? HttpStatusCode.OK : this.statusCode;

            if (querySucceeded)
            {
                serverGVFSConfig = new ServerGVFSConfig();
                errorMessage = null;
                return true;
            }

            serverGVFSConfig = null;
            errorMessage = "Mock config query failure";
            return false;
        }

        public void Dispose()
        {
        }
    }
}
