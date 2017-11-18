using RGFS.Common.Tracing;
using System;
using System.Text;

namespace RGFS.Common.Git
{
    public class GitAuthentication
    {
        private const double MaxBackoffSeconds = 30;
        private readonly object gitAuthLock = new object();
        private int numberOfAttempts = 0;
        private DateTime lastAuthAttempt = DateTime.MinValue;

        private string cachedAuthString;

        private GitProcess git;

        public GitAuthentication(Enlistment enlistment)
            : this(new GitProcess(enlistment))
        {
        }

        public GitAuthentication(GitProcess git)
        {
            this.git = git;
        }

        public bool IsBackingOff
        {
            get
            {
                return this.GetNextAuthAttemptTime() > DateTime.Now;
            }
        }

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

                    this.git.RevokeCredential();
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

                        if (!this.git.TryGetCredentials(tracer, out gitUsername, out gitPassword))
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
                }
            }

            errorMessage = null;
            return true;
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
