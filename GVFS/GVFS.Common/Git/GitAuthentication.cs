using GVFS.Common.Tracing;
using System;
using System.Text;

namespace GVFS.Common.Git
{
    public class GitAuthentication
    {
        private readonly object gitAuthLock = new object();

        private int numberOfRetries = -1;
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

        public void ConfirmCredentialsWorked(string usedCredential)
        {
            lock (this.gitAuthLock)
            {
                if (usedCredential == this.cachedAuthString)
                {
                    this.numberOfRetries = -1;
                    this.lastAuthAttempt = DateTime.MinValue;
                }
            }
        }

        public bool RevokeAndCheckCanRetry(string usedCredential)
        {
            lock (this.gitAuthLock)
            {
                if (usedCredential != this.cachedAuthString)
                {
                    // Don't stomp a different credential
                    return true;
                }

                if (this.cachedAuthString != null)
                {
                    // Wipe the username and password so we can try recovering if applicable.
                    this.cachedAuthString = null;

                    this.git.RevokeCredential();
                    this.UpdateBackoff();
                }

                return !this.IsBackingOff();
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

                        // These auth settings are necessary to support running the functional tests on build servers.
                        // The reason it's needed is that the GVFS.Service runs as LocalSystem, and the build agent does not
                        // so storing the agent's creds in the Windows Credential Store does not allow the service to discover it
                        GitProcess.Result usernameResult = this.git.GetFromConfig("gvfs.FunctionalTests.UserName");
                        GitProcess.Result passwordResult = this.git.GetFromConfig("gvfs.FunctionalTests.Password");

                        if (!usernameResult.HasErrors &&
                            !passwordResult.HasErrors)
                        {
                            gitUsername = usernameResult.Output.TrimEnd('\n');
                            gitPassword = passwordResult.Output.TrimEnd('\n');
                        }
                        else
                        {
                            if (this.IsBackingOff())
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
                        }

                        this.cachedAuthString = Convert.ToBase64String(Encoding.ASCII.GetBytes(gitUsername + ":" + gitPassword));
                    }

                    gitAuthString = this.cachedAuthString;
                }
            }

            errorMessage = null;
            return true;
        }

        private bool IsBackingOff()
        {
            return this.GetNextAuthAttemptTime() > DateTime.Now;
        }

        private DateTime GetNextAuthAttemptTime()
        {
            switch (this.numberOfRetries)
            {
                case -1:
                case 0:
                    return DateTime.MinValue;
                case 1:
                    return this.lastAuthAttempt + TimeSpan.FromSeconds(30);
                default:
                    return DateTime.MaxValue;
            }
        }

        private void UpdateBackoff()
        {
            this.lastAuthAttempt = DateTime.Now;
            this.numberOfRetries++;
        }
    }
}
