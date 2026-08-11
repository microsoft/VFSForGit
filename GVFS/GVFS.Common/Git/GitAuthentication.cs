using GVFS.Common.Http;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;

namespace GVFS.Common.Git
{
    public class GitAuthentication
    {
        private const double MaxBackoffSeconds = 30;
        public const int DefaultCredentialTimeoutMs = 30_000;
        public const int BackgroundCredentialTimeoutMs = 120_000;

        /// <summary>
        /// Minimum time to wait for an in-flight credential fetch before giving up on
        /// serialization. The effective wait is never shorter than the fetch timeout
        /// itself, so a slow but legitimate prompt cannot cause a second prompt.
        /// </summary>
        private const int DefaultCredentialGateWaitMs = 60_000;

        private readonly Lock gitAuthLock = new Lock();
        private readonly SemaphoreSlim credentialGate = new SemaphoreSlim(1, 1);
        private readonly ICredentialStore credentialStore;
        private readonly string repoUrl;

        // Signaled when initialization completes (success or failure). Callers of
        // TryGetCredentials that arrive before initialization is done wait on this
        // rather than throwing. This matters when mount starts virtualization while
        // auth initialization is still running in the background (cache-server path):
        // a directory enumeration can call TryGetCredentials before the background
        // TryInitializeAndQueryGVFSConfig has set isInitialized.
        private readonly ManualResetEventSlim initializationComplete = new ManualResetEventSlim(false);

        private int numberOfAttempts = 0;
        private DateTime lastAuthAttempt = DateTime.MinValue;

        private string cachedCredentialString;
        private bool isCachedCredentialStringApproved = false;

        private bool isInitialized;

        public GitAuthentication(GitProcess git, string repoUrl)
        {
            this.credentialStore = git;
            this.repoUrl = repoUrl;

            if (git.TryGetConfigUrlMatch("http", this.repoUrl, out Dictionary<string, GitConfigSetting> configSettings))
            {
                this.GitSsl = new GitSsl(configSettings);
            }
        }

        public bool IsBackingOff
        {
            get
            {
                return this.GetNextAuthAttemptTime() > DateTime.Now;
            }
        }

        public bool IsAnonymous { get; private set; } = true;

        /// <summary>
        /// How long a caller of <see cref="TryGetCredentials"/> will wait for
        /// initialization to complete before giving up with a retryable error.
        /// Defaults to the maximum time initialization itself can take (a
        /// background credential fetch). Overridable for tests.
        /// </summary>
        internal int InitializationWaitTimeoutMs { get; set; } = BackgroundCredentialTimeoutMs;

        private GitSsl GitSsl { get; }

        /// <summary>
        /// Test-only hook to force the anonymous state. Production code determines this
        /// by probing the server in <see cref="TryInitializeAndQueryGVFSConfig"/>.
        /// </summary>
        internal void SetIsAnonymousForTesting(bool isAnonymous)
        {
            this.IsAnonymous = isAnonymous;
        }

        public void ApproveCredentials(ITracer tracer, string credentialString, int credentialTimeoutMs = DefaultCredentialTimeoutMs, CancellationToken cancellationToken = default)
        {
            lock (this.gitAuthLock)
            {
                // Don't reset the backoff if this is for a different credential than we have cached
                if (credentialString == this.cachedCredentialString)
                {
                    this.numberOfAttempts = 0;
                    this.lastAuthAttempt = DateTime.MinValue;

                    // Tell Git to store the valid credential if we haven't already
                    // done so for this cached credential.
                    if (!this.isCachedCredentialStringApproved)
                    {
                        string username;
                        string password;
                        if (TryParseCredentialString(this.cachedCredentialString, out username, out password))
                        {
                            if (!this.credentialStore.TryStoreCredential(tracer, this.repoUrl, username, password, out string error, credentialTimeoutMs, cancellationToken))
                            {
                                // Storing credentials is best effort attempt - log failure, but do not fail
                                tracer.RelatedWarning("Failed to store credential string: {0}", error);
                            }

                            this.isCachedCredentialStringApproved = true;
                        }
                        else
                        {
                            EventMetadata metadata = new EventMetadata(new Dictionary<string, object>
                            {
                                ["RepoUrl"] = this.repoUrl,
                            });
                            tracer.RelatedError(metadata, "Failed to parse credential string for approval");
                        }
                    }
                }
            }
        }

        public void RejectCredentials(ITracer tracer, string credentialString, int credentialTimeoutMs = DefaultCredentialTimeoutMs, CancellationToken cancellationToken = default)
        {
            lock (this.gitAuthLock)
            {
                string cachedCredentialAtStartOfReject = this.cachedCredentialString;
                // Don't stomp a different credential
                if (credentialString == cachedCredentialAtStartOfReject && cachedCredentialAtStartOfReject != null)
                {
                    // We can't assume that the credential store's cached credential is the same as the one we have.
                    // Reload the credential from the store to ensure we're rejecting the correct one.
                    int attemptsBeforeCheckingExistingCredential = this.numberOfAttempts;
                    if (this.TryCallGitCredential(tracer, out string getCredentialError, out _, credentialTimeoutMs, cancellationToken))
                    {
                        if (this.cachedCredentialString != cachedCredentialAtStartOfReject)
                        {
                            // If the store already had a different credential, we don't want to reject it without trying it.
                            this.isCachedCredentialStringApproved = false;
                            return;
                        }
                    }
                    else
                    {
                        tracer.RelatedWarning(getCredentialError);
                    }

                    // If we can we should pass the actual username/password values we used (and found to be invalid)
                    // to `git-credential reject` so the credential helpers can attempt to check if they're erasing
                    // the expected credentials, if they so choose to.
                    string username;
                    string password;
                    if (TryParseCredentialString(this.cachedCredentialString, out username, out password))
                    {
                        if (!this.credentialStore.TryDeleteCredential(tracer, this.repoUrl, username, password, out string error, credentialTimeoutMs, cancellationToken))
                        {
                            // Deleting credentials is best effort attempt - log failure, but do not fail
                            tracer.RelatedWarning("Failed to delete credential string: {0}", error);
                        }
                    }
                    else
                    {
                        // We failed to parse the credential string so instead (as a recovery) we try to erase without
                        // specifying the particular username/password.
                        EventMetadata metadata = new EventMetadata(new Dictionary<string, object>
                        {
                            ["RepoUrl"] = this.repoUrl,
                        });
                        tracer.RelatedWarning(metadata, "Failed to parse credential string for rejection. Rejecting any credential for this repo URL.");
                        this.credentialStore.TryDeleteCredential(tracer, this.repoUrl, username: null, password: null, error: out string error, timeoutMs: credentialTimeoutMs, cancellationToken: cancellationToken);
                    }

                    this.cachedCredentialString = null;
                    this.isCachedCredentialStringApproved = false;

                    // Backoff may have already been incremented by a failure in TryCallGitCredential
                    if (attemptsBeforeCheckingExistingCredential == this.numberOfAttempts)
                    {
                        this.UpdateBackoff();
                    }
                }
            }
        }

        public bool TryGetCredentials(ITracer tracer, out string credentialString, out string errorMessage, int credentialTimeoutMs = DefaultCredentialTimeoutMs, CancellationToken cancellationToken = default)
        {
            return this.TryGetCredentials(tracer, out credentialString, out errorMessage, out _, credentialTimeoutMs, cancellationToken);
        }

        /// <summary>
        /// Fetches credentials, reporting via <paramref name="timedOut"/> whether the failure was
        /// the credential manager exceeding its bound rather than a genuine auth failure. Callers
        /// use this to avoid immediately retrying, which would re-prompt the user.
        /// </summary>
        public bool TryGetCredentials(ITracer tracer, out string credentialString, out string errorMessage, out bool timedOut, int credentialTimeoutMs = DefaultCredentialTimeoutMs, CancellationToken cancellationToken = default)
        {
            timedOut = false;

            if (!this.isInitialized)
            {
                // Initialization may still be running in the background (mount can
                // start virtualization before the background auth/config query has
                // finished when a cache server is configured). Wait for it to
                // complete rather than throwing an unhandled exception into the
                // virtualization callback, which would crash the mount process.
                if (!this.initializationComplete.Wait(this.InitializationWaitTimeoutMs))
                {
                    credentialString = null;
                    errorMessage = "Timed out waiting for authentication to initialize";
                    return false;
                }
            }

            credentialString = this.cachedCredentialString;
            if (credentialString == null)
            {
                lock (this.gitAuthLock)
                {
                    if (this.cachedCredentialString == null)
                    {
                        if (this.IsBackingOff)
                        {
                            errorMessage = "Auth failed. No retries will be made until: " + this.GetNextAuthAttemptTime();
                            return false;
                        }

                        if (!this.TryCallGitCredential(tracer, out errorMessage, out timedOut, credentialTimeoutMs, cancellationToken))
                        {
                            return false;
                        }
                    }

                    credentialString = this.cachedCredentialString;
                }
            }

            errorMessage = null;
            return true;
        }

        /// <summary>
        /// Initialize authentication by probing the server. Determines whether
        /// anonymous access is supported and, if not, fetches credentials.
        /// Callers that also need the GVFS config should use
        /// <see cref="TryInitializeAndQueryGVFSConfig"/> instead to avoid a
        /// redundant HTTP round-trip.
        /// </summary>
        public bool TryInitialize(ITracer tracer, Enlistment enlistment, out string errorMessage)
        {
            // Delegate to the combined method, discarding the config result.
            // This avoids duplicating the anonymous-probe + credential-fetch logic.
            return this.TryInitializeAndQueryGVFSConfig(
                tracer,
                enlistment,
                new RetryConfig(),
                out _,
                out errorMessage,
                out _);
        }

        /// <summary>
        /// Combines authentication initialization with the GVFS config query,
        /// eliminating a redundant HTTP round-trip. The anonymous probe and
        /// config query use the same request to /gvfs/config:
        ///   1. Config query → /gvfs/config → 200 (anonymous) or 401
        ///   2. If 401: credential fetch, then retry → 200
        /// This saves one HTTP request compared to probing auth separately
        /// and then querying config, and reuses the same TCP/TLS connection.
        /// </summary>
        public bool TryInitializeAndQueryGVFSConfig(
            ITracer tracer,
            Enlistment enlistment,
            RetryConfig retryConfig,
            out ServerGVFSConfig serverGVFSConfig,
            out string errorMessage,
            out bool isAuthFailure,
            int credentialTimeoutMs = DefaultCredentialTimeoutMs)
        {
            if (this.isInitialized)
            {
                throw new InvalidOperationException("Already initialized");
            }

            serverGVFSConfig = null;
            errorMessage = null;
            isAuthFailure = false;

            using (ConfigHttpRequestor configRequestor = new ConfigHttpRequestor(tracer, enlistment, retryConfig))
            {
                HttpStatusCode? httpStatus;

                // First attempt without credentials. If anonymous access works,
                // we get the config in a single request.
                if (configRequestor.TryQueryGVFSConfig(false, out serverGVFSConfig, out httpStatus, out _))
                {
                    this.IsAnonymous = true;
                    this.MarkInitialized();
                    tracer.RelatedInfo("{0}: Anonymous access succeeded, config obtained in one request", nameof(this.TryInitializeAndQueryGVFSConfig));
                    return true;
                }

                if (httpStatus != HttpStatusCode.Unauthorized)
                {
                    this.MarkInitialized();
                    errorMessage = "Unable to query /gvfs/config";
                    tracer.RelatedWarning("{0}: Config query failed with status {1}", nameof(this.TryInitializeAndQueryGVFSConfig), httpStatus?.ToString() ?? "None");
                    return false;
                }

                // Server requires authentication — fetch credentials
                this.IsAnonymous = false;

                if (!this.TryCallGitCredential(tracer, out errorMessage, out _, credentialTimeoutMs))
                {
                    isAuthFailure = true;
                    // Mark initialized even on failure so TryGetCredentials can
                    // retry later (e.g., when mount proceeds with a cache server
                    // and object downloads need auth).
                    this.MarkInitialized();
                    tracer.RelatedWarning("{0}: Credential fetch failed: {1}", nameof(this.TryInitializeAndQueryGVFSConfig), errorMessage);
                    return false;
                }

                this.MarkInitialized();

                // Retry with credentials using the same ConfigHttpRequestor (reuses HttpClient/connection)
                HttpStatusCode? retryHttpStatus;
                if (configRequestor.TryQueryGVFSConfig(true, out serverGVFSConfig, out retryHttpStatus, out errorMessage))
                {
                    tracer.RelatedInfo("{0}: Config obtained with credentials", nameof(this.TryInitializeAndQueryGVFSConfig));
                    return true;
                }

                if (retryHttpStatus == HttpStatusCode.Unauthorized || retryHttpStatus == HttpStatusCode.Forbidden)
                {
                    isAuthFailure = true;
                }

                tracer.RelatedWarning("{0}: Config query failed with credentials: {1}", nameof(this.TryInitializeAndQueryGVFSConfig), errorMessage);
                return false;
            }
        }

        /// <summary>
        /// Test-only initialization that skips the network probe and goes
        /// straight to credential fetch. Not for production use.
        /// </summary>
        internal bool TryInitializeAndRequireAuth(ITracer tracer, out string errorMessage)
        {
            if (this.isInitialized)
            {
                throw new InvalidOperationException("Already initialized");
            }

            if (this.TryCallGitCredential(tracer, out errorMessage, out _))
            {
                this.MarkInitialized();
                return true;
            }

            return false;
        }

        public void ConfigureHttpClientHandlerSslIfNeeded(ITracer tracer, HttpClientHandler httpClientHandler, GitProcess gitProcess)
        {
            X509Certificate2 cert = this.GitSsl?.GetCertificate(tracer, gitProcess);
            if (cert != null)
            {
                if (this.GitSsl != null && !this.GitSsl.ShouldVerify)
                {
                    httpClientHandler.ServerCertificateCustomValidationCallback = // CodeQL [SM02184] TLS verification can be disabled by Git itself, so this is just mirroring a feature already exposed.
                        (httpRequestMessage, c, cetChain, policyErrors) =>
                        {
                            return true;
                        };
                }

                httpClientHandler.ClientCertificateOptions = ClientCertificateOption.Manual;
                httpClientHandler.ClientCertificates.Add(cert);
            }
        }

        public void ConfigureSocketsHandlerSslIfNeeded(ITracer tracer, SocketsHttpHandler socketsHandler, GitProcess gitProcess)
        {
            X509Certificate2 cert = this.GitSsl?.GetCertificate(tracer, gitProcess);
            if (cert != null)
            {
                System.Net.Security.SslClientAuthenticationOptions sslOptions = new System.Net.Security.SslClientAuthenticationOptions();

                if (this.GitSsl != null && !this.GitSsl.ShouldVerify)
                {
                    sslOptions.RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true; // CodeQL [SM02184] TLS verification can be disabled by Git itself, so this is just mirroring a feature already exposed.
                }

                sslOptions.ClientCertificates = new System.Security.Cryptography.X509Certificates.X509CertificateCollection { cert };
                socketsHandler.SslOptions = sslOptions;
            }
        }

        private static bool TryParseCredentialString(string credentialString, out string username, out string password)
        {
            if (credentialString != null)
            {
                byte[] data = Convert.FromBase64String(credentialString);
                string rawCredString = Encoding.ASCII.GetString(data);

                string[] usernamePassword = rawCredString.Split(':');
                if (usernamePassword.Length == 2)
                {
                    username = usernamePassword[0];
                    password = usernamePassword[1];

                    return true;
                }
            }

            username = null;
            password = null;
            return false;
        }

        private DateTime GetNextAuthAttemptTime()
        {
            if (this.numberOfAttempts <= 1)
            {
                return DateTime.MinValue;
            }

            double backoffSeconds = RetryBackoff.CalculateBackoffSeconds(this.numberOfAttempts, MaxBackoffSeconds);
            return this.lastAuthAttempt + TimeSpan.FromSeconds(backoffSeconds);
        }

        private void UpdateBackoff()
        {
            this.lastAuthAttempt = DateTime.Now;
            this.numberOfAttempts++;
        }

        private void MarkInitialized()
        {
            // Publish isInitialized before signaling so a waiter that wakes up
            // observes the initialized state.
            this.isInitialized = true;
            this.initializationComplete.Set();
        }

        private bool TryCallGitCredential(ITracer tracer, out string errorMessage, out bool timedOut, int timeoutMs = -1, CancellationToken cancellationToken = default)
        {
            // Serialize credential fetches so only one git-credential-fill
            // process runs at a time. Without this, a background auth task
            // and a foreground object download could both spawn GCM prompts.
            // Wait at least as long as the fetch itself may take; otherwise the
            // gate would expire while the in-flight fetch is still legitimately
            // waiting on the user, and we would fall through and spawn a second
            // competing GCM prompt in exactly the slow-prompt case this bound
            // exists to tolerate.
            int gateTimeoutMs = timeoutMs < 0 ? DefaultCredentialGateWaitMs : Math.Max(DefaultCredentialGateWaitMs, timeoutMs);
            bool acquired = this.credentialGate.Wait(gateTimeoutMs, cancellationToken);
            timedOut = false;
            try
            {
                string gitUsername;
                string gitPassword;
                if (!this.credentialStore.TryGetCredential(tracer, this.repoUrl, out gitUsername, out gitPassword, out errorMessage, out timedOut, timeoutMs, cancellationToken))
                {
                    this.UpdateBackoff();
                    return false;
                }

                if (!string.IsNullOrEmpty(gitUsername) && !string.IsNullOrEmpty(gitPassword))
                {
                    this.cachedCredentialString = Convert.ToBase64String(Encoding.ASCII.GetBytes(gitUsername + ":" + gitPassword));
                    this.isCachedCredentialStringApproved = false;
                }
                else
                {
                    errorMessage = "Got back empty credentials from git";
                    return false;
                }

                return true;
            }
            finally
            {
                if (acquired)
                {
                    this.credentialGate.Release();
                }
            }
        }
    }
}
