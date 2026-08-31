using GVFS.Common;
using GVFS.Common.Http;
using System.Net;

namespace GVFS.UnitTests.Mock.Http
{
    /// <summary>
    /// Drives <see cref="GVFS.Common.Git.GitAuthentication.TryInitializeAndQueryGVFSConfig"/>
    /// with a fixed /gvfs/config probe outcome so the anonymous / authenticated /
    /// indeterminate branches can be tested without a server.
    /// </summary>
    public class MockGVFSConfigRequestor : IGVFSConfigRequestor
    {
        private readonly bool succeedAnonymously;
        private readonly HttpStatusCode? statusCode;

        private MockGVFSConfigRequestor(bool succeedAnonymously, HttpStatusCode? statusCode)
        {
            this.succeedAnonymously = succeedAnonymously;
            this.statusCode = statusCode;
        }

        public int QueryCount { get; private set; }

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
        /// The probe failed for a reason that says nothing about authentication, so
        /// whether the server allows anonymous access is unknown. Pass null for
        /// <paramref name="statusCode"/> to model a failure with no HTTP response at all.
        /// </summary>
        public static MockGVFSConfigRequestor Indeterminate(HttpStatusCode? statusCode)
        {
            return new MockGVFSConfigRequestor(succeedAnonymously: false, statusCode: statusCode);
        }

        public bool TryQueryGVFSConfig(bool logErrors, out ServerGVFSConfig serverGVFSConfig, out HttpStatusCode? httpStatus, out string errorMessage)
        {
            this.QueryCount++;

            httpStatus = this.statusCode;

            if (this.succeedAnonymously)
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
