using GVFS.Common.Http;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;

namespace GVFS.Common.Git
{
    public class GitAuthentication
    {
        private enum AuthenticationState
        {
            Unknown = 0,
            Anonymous,
            Authenticated,
        }

        private const double MaxBackoffSeconds = 30;
        private const int CredentialTimeoutBackoffAttempt = 5;
        public const int DefaultCredentialTimeoutMs = 30_000;
        public const int BackgroundCredentialTimeoutMs = 120_000;

        /// <summary>
        /// Minimum time to wait for an in-flight credential fetch before giving up on
        /// serialization. The effective wait is never shorter than the fetch timeout.
        /// </summary>
        private const int DefaultCredentialGateWaitMs = 60_000;

        private readonly SemaphoreSlim gitAuthLock = new SemaphoreSlim(1, 1);
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
        private bool backoffIsCredentialTimeout;

        private string cachedCredentialString;
        private bool isCachedCredentialStringApproved = false;

        private bool isInitialized;
        private int authenticationState;
        private int initializationStarted;

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

        /// <summary>
        /// True only when the server is known to allow anonymous access.
        /// </summary>
        /// <remarks>
        /// This is false while the state is unknown. While true,
        /// <see cref="Http.HttpRequestor.SendRequest"/> omits the Authorization
        /// header and never calls <see cref="TryGetCredentials"/>.
        /// </remarks>
        public bool IsAnonymous
        {
            get => this.GetAuthenticationState() == AuthenticationState.Anonymous;
        }

        public bool IsSendingAnonymousRequests => this.ShouldSendAnonymous;

        internal bool ShouldSendAnonymous =>
            !this.IsInitializationStarted ||
            (this.initializationComplete.IsSet && this.GetAuthenticationState() != AuthenticationState.Authenticated);

        /// <summary>
        /// Indicates whether authentication initialization has started.
        /// </summary>
        internal bool IsInitializationStarted => Volatile.Read(ref this.initializationStarted) != 0;

        /// <summary>
        /// Marks authentication initialization as scheduled so that requests wait
        /// for its result instead of being sent anonymously.
        /// </summary>
        internal void MarkInitializationStarted()
        {
            Interlocked.Exchange(ref this.initializationStarted, 1);
        }

        /// <summary>
        /// How long a caller of <see cref="TryGetCredentials"/> will wait for
        /// initialization to complete before giving up with a retryable error.
        /// Defaults to the maximum time initialization itself can take (a
        /// background credential fetch). Overridable for tests.
        /// </summary>
        internal int InitializationWaitTimeoutMs { get; set; } = BackgroundCredentialTimeoutMs;

        /// <summary>
        /// Test seam for the /gvfs/config probe. When null, production code uses
        /// <see cref="ConfigHttpRequestor"/>. Unit tests substitute a fake so the
        /// probe outcome - anonymous success, 401, or an indeterminate network
        /// failure - can be driven deterministically.
        /// </summary>
        internal IGVFSConfigRequestor ConfigRequestorOverride { get; set; }

        internal int CredentialGateWaitTimeoutMs { get; set; } = DefaultCredentialGateWaitMs;

        private GitSsl GitSsl { get; }

        /// <summary>
        /// Test-only hook to force the anonymous state. Production code determines this
        /// by probing the server in <see cref="TryInitializeAndQueryGVFSConfig"/>.
        /// </summary>
        internal void SetIsAnonymousForTesting(bool isAnonymous)
        {
            if (isAnonymous)
            {
                this.MarkAnonymousAccessSucceeded();
            }
            else
            {
                this.MarkAuthenticationRequired();
            }

            this.MarkInitialized();
        }

        public void ApproveCredentials(ITracer tracer, string credentialString, int credentialTimeoutMs = DefaultCredentialTimeoutMs, CancellationToken cancellationToken = default)
        {
            using (new SemaphoreScope(this.gitAuthLock, cancellationToken))
            {
                // Don't reset the backoff if this is for a different credential than we have cached
                if (credentialString == this.cachedCredentialString)
                {
                    this.numberOfAttempts = 0;
                    this.lastAuthAttempt = DateTime.MinValue;
                    this.backoffIsCredentialTimeout = false;

                    // Tell Git to store the valid credential if we haven't already
                    // done so for this cached credential.
                    if (!this.isCachedCredentialStringApproved)
                    {
                        string username;
                        string password;
                        if (TryParseCredentialString(this.cachedCredentialString, out username, out password))
                        {
                            if (!this.TryStoreCredential(tracer, username, password, out string error, out bool timedOut, credentialTimeoutMs, cancellationToken))
                            {
                                // Storing credentials is best effort attempt - log failure, but do not fail
                                tracer.RelatedWarning("Failed to store credential string: {0}", error);
                                if (timedOut)
                                {
                                    this.TraceCredentialOperationTimeout(tracer, "approve", credentialTimeoutMs);
                                }
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

        public bool RejectCredentials(ITracer tracer, string credentialString, int credentialTimeoutMs = DefaultCredentialTimeoutMs, CancellationToken cancellationToken = default)
        {
            using (new SemaphoreScope(this.gitAuthLock, cancellationToken))
            {
                Stopwatch credentialStopwatch = Stopwatch.StartNew();
                string cachedCredentialAtStartOfReject = this.cachedCredentialString;
                // Don't stomp a different credential
                if (credentialString == cachedCredentialAtStartOfReject && cachedCredentialAtStartOfReject != null)
                {
                    // We can't assume that the credential store's cached credential is the same as the one we have.
                    // Reload the credential from the store to ensure we're rejecting the correct one.
                    int attemptsBeforeCheckingExistingCredential = this.numberOfAttempts;
                    int remainingTimeoutMs = GetRemainingTimeoutMs(credentialTimeoutMs, credentialStopwatch);
                    if (this.TryCallGitCredential(tracer, out string getCredentialError, out bool reloadTimedOut, remainingTimeoutMs, cancellationToken))
                    {
                        if (this.cachedCredentialString != cachedCredentialAtStartOfReject)
                        {
                            // If the store already had a different credential, we don't want to reject it without trying it.
                            this.isCachedCredentialStringApproved = false;
                            return false;
                        }
                    }
                    else
                    {
                        tracer.RelatedWarning(getCredentialError);
                        if (reloadTimedOut)
                        {
                            this.ClearCachedCredential();
                            this.TraceCredentialOperationTimeout(tracer, "reload", credentialTimeoutMs);
                            return true;
                        }
                    }

                    // If we can we should pass the actual username/password values we used (and found to be invalid)
                    // to `git-credential reject` so the credential helpers can attempt to check if they're erasing
                    // the expected credentials, if they so choose to.
                    string username;
                    string password;
                    if (TryParseCredentialString(this.cachedCredentialString, out username, out password))
                    {
                        remainingTimeoutMs = GetRemainingTimeoutMs(credentialTimeoutMs, credentialStopwatch);
                        if (remainingTimeoutMs == 0)
                        {
                            this.UpdateBackoff(requireDelay: true, credentialOperationTimedOut: true);
                            this.ClearCachedCredential();
                            this.TraceCredentialOperationTimeout(tracer, "delete", credentialTimeoutMs);
                            return true;
                        }

                        if (!this.TryDeleteCredential(tracer, username, password, out string error, out bool deleteTimedOut, remainingTimeoutMs, CancellationToken.None))
                        {
                            // Deleting credentials is best effort attempt - log failure, but do not fail
                            tracer.RelatedWarning("Failed to delete credential string: {0}", error);
                            if (deleteTimedOut)
                            {
                                this.UpdateBackoff(requireDelay: true, credentialOperationTimedOut: true);
                                this.ClearCachedCredential();
                                this.TraceCredentialOperationTimeout(tracer, "delete", credentialTimeoutMs);
                                return true;
                            }
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
                        remainingTimeoutMs = GetRemainingTimeoutMs(credentialTimeoutMs, credentialStopwatch);
                        if (remainingTimeoutMs == 0)
                        {
                            this.UpdateBackoff(requireDelay: true, credentialOperationTimedOut: true);
                            this.ClearCachedCredential();
                            this.TraceCredentialOperationTimeout(tracer, "delete", credentialTimeoutMs);
                            return true;
                        }

                        this.TryDeleteCredential(tracer, username: null, password: null, error: out string error, timedOut: out bool deleteTimedOut, timeoutMs: remainingTimeoutMs, cancellationToken: CancellationToken.None);
                        if (deleteTimedOut)
                        {
                            this.UpdateBackoff(requireDelay: true, credentialOperationTimedOut: true);
                            this.ClearCachedCredential();
                            this.TraceCredentialOperationTimeout(tracer, "delete", credentialTimeoutMs);
                            return true;
                        }
                    }

                    this.ClearCachedCredential();

                    // Backoff may have already been incremented by a failure in TryCallGitCredential
                    if (attemptsBeforeCheckingExistingCredential == this.numberOfAttempts)
                    {
                        this.UpdateBackoff();
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Gets the credential to send with a request.
        /// </summary>
        /// <param name="credentialString">
        /// The Basic credential, or null when this returns true because the server
        /// allows anonymous access or its authentication requirement remains unknown.
        /// Callers must check for null before using it.
        /// </param>
        /// <param name="errorMessage">The failure reason when this returns false.</param>
        /// <returns>
        /// True when the request can proceed: with the credential in
        /// <paramref name="credentialString"/>, or anonymously when it is null.
        /// </returns>
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
                if (!this.initializationComplete.Wait(this.InitializationWaitTimeoutMs, cancellationToken))
                {
                    credentialString = null;
                    errorMessage = "Timed out waiting for authentication to initialize";
                    return false;
                }
            }

            if (this.GetAuthenticationState() != AuthenticationState.Authenticated)
            {
                credentialString = null;
                errorMessage = null;
                return true;
            }

            credentialString = this.cachedCredentialString;
            if (credentialString == null)
            {
                using (new SemaphoreScope(this.gitAuthLock, cancellationToken))
                {
                    if (this.cachedCredentialString == null)
                    {
                        if (this.IsBackingOff)
                        {
                            timedOut = this.backoffIsCredentialTimeout;
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

            this.MarkInitializationStarted();

            try
            {
                return this.TryInitializeAndQueryGVFSConfigCore(
                    tracer,
                    enlistment,
                    retryConfig,
                    out serverGVFSConfig,
                    out errorMessage,
                    out isAuthFailure,
                    credentialTimeoutMs);
            }
            finally
            {
                if (!this.isInitialized)
                {
                    this.MarkInitialized();
                }
            }
        }

        private bool TryInitializeAndQueryGVFSConfigCore(
            ITracer tracer,
            Enlistment enlistment,
            RetryConfig retryConfig,
            out ServerGVFSConfig serverGVFSConfig,
            out string errorMessage,
            out bool isAuthFailure,
            int credentialTimeoutMs)
        {
            serverGVFSConfig = null;
            errorMessage = null;
            isAuthFailure = false;

            IGVFSConfigRequestor configRequestor = this.ConfigRequestorOverride ?? new ConfigHttpRequestor(tracer, enlistment, retryConfig);
            using (configRequestor)
            {
                HttpStatusCode? httpStatus;
                string probeError;

                // The probe must not consult the authentication state it determines.
                if (configRequestor.TryQueryGVFSConfig(false, out serverGVFSConfig, out httpStatus, out probeError, forceAnonymous: true))
                {
                    this.MarkAnonymousAccessSucceeded();
                    this.MarkInitialized();
                    tracer.RelatedInfo("{0}: Anonymous access succeeded, config obtained in one request", nameof(this.TryInitializeAndQueryGVFSConfig));
                    return true;
                }

                if (httpStatus != HttpStatusCode.Unauthorized)
                {
                    // The probe did not determine whether the server allows anonymous
                    // access. Keep the state unknown so later requests can try
                    // anonymously and promote to authenticated if the server rejects them.
                    this.MarkInitialized();
                    errorMessage = "Unable to query /gvfs/config" +
                        (string.IsNullOrEmpty(probeError) ? string.Empty : ": " + probeError) +
                        ". The authentication requirement remains unknown.";

                    // The probe is unauthenticated, so the error text carries no credential.
                    EventMetadata indeterminateMetadata = new EventMetadata(new Dictionary<string, object>
                    {
                        ["Area"] = nameof(GitAuthentication),
                        ["HttpStatus"] = httpStatus?.ToString() ?? "None",
                        ["ProbeError"] = probeError ?? string.Empty,
                        ["AuthenticationState"] = AuthenticationState.Unknown.ToString(),
                    });
                    tracer.RelatedWarning(
                        indeterminateMetadata,
                        $"{nameof(this.TryInitializeAndQueryGVFSConfig)}: Config query failed with status {httpStatus?.ToString() ?? "None"}; authentication requirement remains unknown",
                        Keywords.Telemetry);
                    return false;
                }

                // Server requires authentication — fetch credentials
                this.MarkAuthenticationRequired();

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
        internal bool TryInitializeAndRequireAuth(ITracer tracer, out string errorMessage, int credentialTimeoutMs = Timeout.Infinite)
        {
            if (this.isInitialized)
            {
                throw new InvalidOperationException("Already initialized");
            }

            this.MarkInitializationStarted();
            this.MarkAuthenticationRequired();

            try
            {
                if (this.TryCallGitCredential(tracer, out errorMessage, out _, credentialTimeoutMs))
                {
                    this.MarkInitialized();
                    return true;
                }

                return false;
            }
            finally
            {
                if (!this.isInitialized)
                {
                    this.MarkInitialized();
                }
            }
        }

        internal void MarkAnonymousAccessSucceeded()
        {
            Interlocked.CompareExchange(
                ref this.authenticationState,
                (int)AuthenticationState.Anonymous,
                (int)AuthenticationState.Unknown);
        }

        internal void MarkAuthenticationRequired()
        {
            Interlocked.Exchange(ref this.authenticationState, (int)AuthenticationState.Authenticated);
        }

        internal bool PromoteToAuthenticationRequired(out string previousAuthenticationState)
        {
            AuthenticationState previousState = (AuthenticationState)Interlocked.Exchange(
                ref this.authenticationState,
                (int)AuthenticationState.Authenticated);

            previousAuthenticationState = previousState.ToString();

            if (Interlocked.CompareExchange(ref this.initializationStarted, 1, 0) == 0)
            {
                this.MarkInitialized();
            }

            return previousState != AuthenticationState.Authenticated;
        }

        private AuthenticationState GetAuthenticationState()
        {
            return (AuthenticationState)Volatile.Read(ref this.authenticationState);
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
                string rawCredString = Encoding.UTF8.GetString(data);

                string[] usernamePassword = rawCredString.Split(new[] { ':' }, 2);
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

        private void UpdateBackoff(bool requireDelay = false, bool credentialOperationTimedOut = false)
        {
            this.lastAuthAttempt = DateTime.Now;
            this.numberOfAttempts++;
            this.backoffIsCredentialTimeout = credentialOperationTimedOut;
            if (requireDelay && credentialOperationTimedOut)
            {
                this.numberOfAttempts = Math.Max(this.numberOfAttempts, CredentialTimeoutBackoffAttempt);
            }
            else if (requireDelay && this.numberOfAttempts == 1)
            {
                this.numberOfAttempts++;
            }
        }

        private static int GetRemainingTimeoutMs(int timeoutMs, Stopwatch stopwatch)
        {
            if (timeoutMs < 0)
            {
                return Timeout.Infinite;
            }

            return (int)Math.Max(0, timeoutMs - stopwatch.ElapsedMilliseconds);
        }

        private void ClearCachedCredential()
        {
            this.cachedCredentialString = null;
            this.isCachedCredentialStringApproved = false;
        }

        private void TraceCredentialOperationTimeout(ITracer tracer, string operation, int timeoutMs)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Operation", operation);
            metadata.Add("timeoutMs", timeoutMs);
            tracer.RelatedEvent(
                EventLevel.Warning,
                "CredentialOperationTimedOut",
                metadata,
                Keywords.Network | Keywords.Telemetry);
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
            cancellationToken.ThrowIfCancellationRequested();
            timedOut = false;

            int gateTimeoutMs = timeoutMs < 0 ? Timeout.Infinite : Math.Max(this.CredentialGateWaitTimeoutMs, timeoutMs);
            if (!this.credentialGate.Wait(gateTimeoutMs, cancellationToken))
            {
                timedOut = true;
                errorMessage = "Timed out waiting for another credential operation";
                this.UpdateBackoff(requireDelay: true, credentialOperationTimedOut: true);
                return false;
            }

            try
            {
                bool succeeded = this.credentialStore.TryGetCredential(
                    tracer,
                    this.repoUrl,
                    out string username,
                    out string password,
                    out errorMessage,
                    out timedOut,
                    timeoutMs,
                    CancellationToken.None);
                if (!succeeded)
                {
                    this.UpdateBackoff(requireDelay: timedOut, credentialOperationTimedOut: timedOut);
                    return false;
                }

                if (!string.IsNullOrEmpty(password))
                {
                    this.cachedCredentialString = Convert.ToBase64String(Encoding.UTF8.GetBytes(username + ":" + password));
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
                this.credentialGate.Release();
            }
        }

        private bool TryStoreCredential(
            ITracer tracer,
            string username,
            string password,
            out string error,
            out bool timedOut,
            int timeoutMs,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int gateTimeoutMs = timeoutMs < 0 ? Timeout.Infinite : Math.Max(this.CredentialGateWaitTimeoutMs, timeoutMs);
            if (!this.credentialGate.Wait(gateTimeoutMs, cancellationToken))
            {
                timedOut = true;
                error = "Timed out waiting for another credential operation";
                return false;
            }

            try
            {
                return this.credentialStore.TryStoreCredential(
                    tracer,
                    this.repoUrl,
                    username,
                    password,
                    out error,
                    out timedOut,
                    timeoutMs,
                    CancellationToken.None);
            }
            finally
            {
                this.credentialGate.Release();
            }
        }

        private bool TryDeleteCredential(
            ITracer tracer,
            string username,
            string password,
            out string error,
            out bool timedOut,
            int timeoutMs,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int gateTimeoutMs = timeoutMs < 0 ? Timeout.Infinite : Math.Max(this.CredentialGateWaitTimeoutMs, timeoutMs);
            if (!this.credentialGate.Wait(gateTimeoutMs, cancellationToken))
            {
                timedOut = true;
                error = "Timed out waiting for another credential operation";
                return false;
            }

            try
            {
                return this.credentialStore.TryDeleteCredential(
                    tracer,
                    this.repoUrl,
                    username,
                    password,
                    out error,
                    out timedOut,
                    timeoutMs,
                    CancellationToken.None);
            }
            finally
            {
                this.credentialGate.Release();
            }
        }

        private readonly struct SemaphoreScope : IDisposable
        {
            private readonly SemaphoreSlim semaphore;

            public SemaphoreScope(SemaphoreSlim semaphore, CancellationToken cancellationToken)
            {
                this.semaphore = semaphore;
                this.semaphore.Wait(cancellationToken);
            }

            public void Dispose()
            {
                this.semaphore.Release();
            }
        }
    }
}
