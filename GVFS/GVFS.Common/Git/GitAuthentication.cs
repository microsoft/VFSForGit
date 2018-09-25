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
        private int numberOfAttempts = 0;
        private DateTime lastAuthAttempt = DateTime.MinValue;

        private string cachedAuthString;

        private GitProcess git;
        private string repoUrl;

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
                    // Wipe the username and password so we can try recovering if applicable.
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
            gitAuthString = this.cachedAuthString;
            if (this.cachedAuthString == null)
            {
                lock (this.gitAuthLock)
                {
                    if (this.cachedAuthString == null)
                    {
                        string gitUsername;
                        string gitPassword;

                        if (this.IsBackingOff)
                        {
                            gitAuthString = null;
                            errorMessage = "Auth failed. No retries will be made until: " + this.GetNextAuthAttemptTime();
                            return false;
                        }

                        if (!this.git.TryGetCredentials(tracer, this.repoUrl, out gitUsername, out gitPassword))
                        {
                            gitAuthString = null;
                            errorMessage = "Authentication failed.";
                            this.UpdateBackoff();
                            return false;
                        }

                        if (!string.IsNullOrEmpty(gitUsername) && !string.IsNullOrEmpty(gitPassword))
                        {
                            this.cachedAuthString = Convert.ToBase64String(Encoding.ASCII.GetBytes(gitUsername + ":" + gitPassword));
                        }
                        else
                        {
                            this.cachedAuthString = string.Empty;
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
            return
                this.TryAnonymousQuery(tracer, enlistment) ||
                this.TryRefreshCredentials(tracer, out errorMessage);
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
    }
}
