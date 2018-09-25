using GVFS.Common.Http;
using GVFS.Common.Tracing;
using System;
using System.Text;

namespace GVFS.Common.Git
{
    public class GitAuthentication
    {
        private const double MaxBackoffSeconds = 30;

        private readonly object gitAuthLock = new object();
        private readonly GitProcess git;
        private readonly string repoUrl;

        private int numberOfAttempts = 0;
        private DateTime lastAuthAttempt = DateTime.MinValue;

        private string cachedAuthString;

        private bool isInitialized;

        public GitAuthentication(GitProcess git, string repoUrl)
        {
            this.git = git;
            this.repoUrl = repoUrl;
        }

        public bool IsBackingOff
        {
            get
            {
                return this.GetNextAuthAttemptTime() > DateTime.Now;
            }
        }

        public bool IsAnonymous { get; private set; } = true;

        public void ConfirmCredentialsWorked(string usedCredential)
        {
            lock (this.gitAuthLock)
            {
                if (usedCredential == this.cachedAuthString)
                {
                    this.numberOfAttempts = 0;
                    this.lastAuthAttempt = DateTime.MinValue;
                }
            }
        }

        public void Revoke(string usedCredential)
        {
            lock (this.gitAuthLock)
            {
                if (usedCredential != this.cachedAuthString)
                {
                    // Don't stomp a different credential
                    return;
                }

                if (this.cachedAuthString != null)
                {
                    this.cachedAuthString = null;
                    this.git.RevokeCredential(this.repoUrl);

                    this.UpdateBackoff();
                }
            }
        }

        public bool TryRefreshCredentials(ITracer tracer, out string errorMessage)
        {
            string authString;
            return this.TryGetCredentials(tracer, out authString, out errorMessage);
        }

        public bool TryGetCredentials(ITracer tracer, out string gitAuthString, out string errorMessage)
        {
            if (!this.isInitialized)
            {
                throw new InvalidOperationException("This auth instance must be initialized before it can be used");
            }

            gitAuthString = this.cachedAuthString;
            if (gitAuthString == null)
            {
                lock (this.gitAuthLock)
                {
                    if (this.cachedAuthString == null)
                    {
                        if (this.IsBackingOff)
                        {
                            errorMessage = "Auth failed. No retries will be made until: " + this.GetNextAuthAttemptTime();
                            return false;
                        }

                        if (!this.TryCallGitCredential(tracer, out errorMessage))
                        {
                            return false;
                        }
                    }

                    gitAuthString = this.cachedAuthString;
                    tracer.RelatedInfo("Received auth token");
                }
            }

            errorMessage = null;
            return true;
        }

        public bool TryInitialize(ITracer tracer, GVFSEnlistment enlistment, out string errorMessage)
        {
            errorMessage = null;

            if (this.TryAnonymousQuery(tracer, enlistment) ||
                this.TryCallGitCredential(tracer, out errorMessage))
            {
                this.isInitialized = true;
                return true;
            }

            return false;
        }

        public bool TryInitializeAndRequireAuth(ITracer tracer, out string errorMessage)
        {
            if (this.TryCallGitCredential(tracer, out errorMessage))
            {
                this.isInitialized = true;
                return true;
            }

            return false;
        }

        private bool TryAnonymousQuery(ITracer tracer, GVFSEnlistment enlistment)
        {
            using (ConfigHttpRequestor configRequestor = new ConfigHttpRequestor(tracer, enlistment, new RetryConfig()))
            {
                ServerGVFSConfig gvfsConfig;
                if (configRequestor.TryQueryGVFSConfig(out gvfsConfig))
                {
                    tracer.RelatedInfo($"Anonymous query to {GVFSConstants.Endpoints.GVFSConfig} succeeded");

                    this.IsAnonymous = true;
                    return true;
                }

                // TODO: We should not lump all errors together here. The query could have failed for a number of
                // reasons unrelated to auth, so we still need to update TryQueryGVFSConfig to pass back a result
                // indicating if the error was caused by a 401. But this is good enough for now to test the behavior.
                tracer.RelatedInfo($"Anonymous query to {GVFSConstants.Endpoints.GVFSConfig} failed");
            }

            this.IsAnonymous = false;
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

        private bool TryCallGitCredential(ITracer tracer, out string errorMessage)
        {
            string gitUsername;
            string gitPassword;
            if (!this.git.TryGetCredentials(tracer, this.repoUrl, out gitUsername, out gitPassword, out errorMessage))
            {
                this.UpdateBackoff();
                return false;
            }

            if (!string.IsNullOrEmpty(gitUsername) && !string.IsNullOrEmpty(gitPassword))
            {
                this.cachedAuthString = Convert.ToBase64String(Encoding.ASCII.GetBytes(gitUsername + ":" + gitPassword));
            }
            else
            {
                // TODO: How can we get back an empty username/password? This may have been an early attempt to "handle" anonymous auth
                // and if so, it should be treated as an error here.
                this.cachedAuthString = string.Empty;
            }

            return true;
        }
    }
}
