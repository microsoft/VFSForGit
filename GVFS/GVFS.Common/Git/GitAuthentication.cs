using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.Text;

namespace GVFS.Common.Git
{
    public class GitAuthentication
    {
        private const int AuthorizationBackoffMinutes = 1;
        private readonly object gitAuthorizationLock = new object();

        private bool credentialHasBeenRevoked = false;
        private string cachedAuthString;
        private Enlistment enlistment;
        private DateTime authRetryBackoff;

        public GitAuthentication(Enlistment enlistment)
        {
            this.enlistment = enlistment;
            this.authRetryBackoff = DateTime.MinValue;
        }

        public void ConfirmCredentialsWorked()
        {
            this.credentialHasBeenRevoked = false;
        }
        
        public bool RevokeAndCheckCanRetry()
        {
            lock (this.gitAuthorizationLock)
            {
                // Wipe the username and password so we can try recovering if applicable.
                this.cachedAuthString = null;
                if (!this.credentialHasBeenRevoked)
                {
                    GitProcess.RevokeCredential(this.enlistment);
                    this.credentialHasBeenRevoked = true;
                    return true;
                }
                else
                {
                    this.authRetryBackoff = DateTime.MaxValue;
                    return false;
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
                lock (this.gitAuthorizationLock)
                {
                    if (this.cachedAuthString == null)
                    {
                        string gitUsername;
                        string gitPassword;

                        // These auth settings are necessary to support running the functional tests on build servers.
                        // The reason it's needed is that the GVFS.Service runs as LocalSystem, and the build agent does not
                        // so storing the agent's creds in the Windows Credential Store does not allow the service to discover it
                        GitProcess git = new GitProcess(this.enlistment);
                        GitProcess.Result usernameResult = git.GetFromConfig("gvfs.FunctionalTests.UserName");
                        GitProcess.Result passwordResult = git.GetFromConfig("gvfs.FunctionalTests.Password");

                        if (!usernameResult.HasErrors &&
                            !passwordResult.HasErrors)
                        {
                            gitUsername = usernameResult.Output.TrimEnd('\n');
                            gitPassword = passwordResult.Output.TrimEnd('\n');
                        }
                        else
                        {
                            bool backingOff = DateTime.Now < this.authRetryBackoff;
                            if (this.credentialHasBeenRevoked)
                            {
                                // Update backoff after an immediate first retry.
                                this.authRetryBackoff = DateTime.Now.AddMinutes(AuthorizationBackoffMinutes);
                            }

                            if (backingOff || !GitProcess.TryGetCredentials(tracer, this.enlistment, out gitUsername, out gitPassword))
                            {
                                gitAuthString = null;
                                errorMessage =
                                    this.authRetryBackoff == DateTime.MinValue
                                    ? "Authorization failed."
                                    : "Authorization failed. No retries will be made until: " + this.authRetryBackoff;
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
    }
}
