using GVFS.Common.Http;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
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

            if (git.TryGetConfigUrlMatch("http", this.repoUrl, out Dictionary<string, GitConfigSetting> configSettings))
            {
                this.GitSsl = new GitSsl(configSettings);
            }
            else
            {
                this.GitSsl = new GitSsl();
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

        private GitSsl GitSsl { get; }

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
                }
            }

            errorMessage = null;
            return true;
        }

        public bool TryInitialize(ITracer tracer, Enlistment enlistment, out string errorMessage)
        {
            if (this.isInitialized)
            {
                throw new InvalidOperationException("Already initialized");
            }

            errorMessage = null;

            bool isAnonymous;
            if (!this.TryAnonymousQuery(tracer, enlistment, out isAnonymous))
            {
                errorMessage = $"Unable to determine if authentication is required";
                return false;
            }

            if (!isAnonymous &&
                !this.TryCallGitCredential(tracer, out errorMessage))
            {
                return false;
            }

            this.IsAnonymous = isAnonymous;
            this.isInitialized = true;
            return true;
        }

        public bool TryInitializeAndRequireAuth(ITracer tracer, out string errorMessage)
        {
            if (this.isInitialized)
            {
                throw new InvalidOperationException("Already initialized");
            }

            if (this.TryCallGitCredential(tracer, out errorMessage))
            {
                this.isInitialized = true;
                return true;
            }

            return false;
        }

        public void SetupSslIfNeeded(ITracer tracer, HttpClientHandler httpClientHandler, GitProcess gitProcess)
        {
            if (!string.IsNullOrEmpty(this.GitSsl.SslCertificate))
            {
                string certificatePassword = null;
                if (this.GitSsl.SslCertPasswordProtected)
                {
                    certificatePassword = this.GitSsl.GetCertificatePassword(tracer, gitProcess);

                    if (string.IsNullOrEmpty(certificatePassword))
                    {
                        tracer.RelatedWarning(
                            new EventMetadata
                            {
                                { "SslCertificate", this.GitSsl.SslCertificate }
                            },
                            "Git config indicates, that certificate is password protected, but retrieved password was null or empty!");
                    }
                }

                X509Certificate2 cert = this.GitSsl.LoadCertificate(tracer, certificatePassword, this.GitSsl.SslVerify);
                if (cert != null)
                {
                    if (!this.GitSsl.SslVerify)
                    {
                        httpClientHandler.ServerCertificateCustomValidationCallback =
                            (httpRequestMessage, c, cetChain, policyErrors) =>
                            {
                                return true;
                            };
                    }

                    httpClientHandler.ClientCertificateOptions = ClientCertificateOption.Manual;
                    httpClientHandler.ClientCertificates.Add(cert);
                }
            }
        }

        private bool TryAnonymousQuery(ITracer tracer, Enlistment enlistment, out bool isAnonymous)
        {
            bool querySucceeded;
            using (ITracer anonymousTracer = tracer.StartActivity("AttemptAnonymousAuth", EventLevel.Informational))
            {
                HttpStatusCode? httpStatus;

                using (ConfigHttpRequestor configRequestor = new ConfigHttpRequestor(anonymousTracer, enlistment, new RetryConfig()))
                {
                    ServerGVFSConfig gvfsConfig;
                    const bool LogErrors = false;
                    if (configRequestor.TryQueryGVFSConfig(LogErrors, out gvfsConfig, out httpStatus, out _))
                    {
                        querySucceeded = true;
                        isAnonymous = true;
                    }
                    else if (httpStatus == HttpStatusCode.Unauthorized)
                    {
                        querySucceeded = true;
                        isAnonymous = false;
                    }
                    else
                    {
                        querySucceeded = false;
                        isAnonymous = false;
                    }
                }

                anonymousTracer.Stop(new EventMetadata
                {
                    { "HttpStatus", httpStatus.HasValue ? ((int)httpStatus).ToString() : "None" },
                    { "QuerySucceeded", querySucceeded },
                    { "IsAnonymous", isAnonymous },
                });
            }

            return querySucceeded;
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
                errorMessage = "Got back empty credentials from git";
                return false;
            }

            return true;
        }
    }
}
