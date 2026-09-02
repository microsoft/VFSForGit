using System;
using System.Net;

namespace GVFS.Common.Http
{
    /// <summary>
    /// Queries the server's /gvfs/config endpoint. Extracted from
    /// <see cref="ConfigHttpRequestor"/> so that
    /// <see cref="Git.GitAuthentication.TryInitializeAndQueryGVFSConfig"/> can be
    /// driven with a deterministic probe outcome in unit tests. Production code
    /// always uses <see cref="ConfigHttpRequestor"/>.
    /// </summary>
    internal interface IGVFSConfigRequestor : IDisposable
    {
        /// <summary>
        /// Queries /gvfs/config.
        /// </summary>
        /// <param name="logErrors">Whether to trace failures as errors.</param>
        /// <param name="serverGVFSConfig">The parsed config when this returns true.</param>
        /// <param name="httpStatus">
        /// The HTTP status the server responded with, or null when the request
        /// never produced an HTTP response (for example a DNS or socket failure).
        /// </param>
        /// <param name="errorMessage">The failure description when this returns false.</param>
        /// <param name="forceAnonymous">
        /// Sends the request without credentials regardless of the authentication
        /// state. The probe that determines whether the server allows anonymous
        /// access must set this.
        /// </param>
        /// <returns>True when the config was retrieved.</returns>
        bool TryQueryGVFSConfig(bool logErrors, out ServerGVFSConfig serverGVFSConfig, out HttpStatusCode? httpStatus, out string errorMessage, bool forceAnonymous = false);
    }
}
