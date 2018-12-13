using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using GVFS.Common.Tracing;

namespace GVFS.Common.Git
{
    public class GitSsl
    {
        private readonly string certificatePathOrSubjectCommonName;
        private readonly bool isCertificatePasswordProtected;

        public GitSsl()
        {
            this.certificatePathOrSubjectCommonName = null;
            this.isCertificatePasswordProtected = false;
            this.ShouldVerify = true;
        }

        public GitSsl(IDictionary<string, GitConfigSetting> configSettings) : this()
        {
            if (configSettings != null)
            {
                if (configSettings.TryGetValue(GitConfigSetting.HttpSslCert, out GitConfigSetting sslCerts))
                {
                    this.certificatePathOrSubjectCommonName = sslCerts.Values.Single();
                }

                if (configSettings.TryGetValue(GitConfigSetting.HttpSslCertPasswordProtected, out GitConfigSetting isSslCertPasswordProtected))
                {
                    this.isCertificatePasswordProtected = isSslCertPasswordProtected.Values.Select(bool.Parse).Single();
                }

                if (configSettings.TryGetValue(GitConfigSetting.HttpSslVerify, out GitConfigSetting sslVerify))
                {
                    this.ShouldVerify = sslVerify.Values.Select(bool.Parse).Single();
                }
            }
        }

        public bool ShouldVerify { get; }

        public string GetCertificatePassword(ITracer tracer, GitProcess git)
        {
            if (git.TryGetCertificatePassword(tracer, this.certificatePathOrSubjectCommonName, out string password, out string error))
            {
                return password;
            }

            return null;
        }

        public X509Certificate2 GetCertificate(ITracer tracer, GitProcess gitProcess)
        {
            if (string.IsNullOrEmpty(this.certificatePathOrSubjectCommonName))
            {
                return null;
            }

            EventMetadata metadata = new EventMetadata
            {
                { "CertificatePathOrSubjectCommonName", this.certificatePathOrSubjectCommonName },
                { "IsCertificatePasswordProtected", this.isCertificatePasswordProtected },
                { "ShouldVerify", this.ShouldVerify }
            };

            X509Certificate2 result =
                this.GetCertificateFromFile(tracer, metadata, gitProcess) ??
                this.GetCertificateFromStore(tracer, metadata);

            if (result == null)
            {
                tracer.RelatedError("Certificate {0} not found", this.certificatePathOrSubjectCommonName);
            }

            return result;
        }

        private static void LogWithAppropriateLevel(ITracer tracer, EventMetadata metadata, IEnumerable<X509Certificate2> certificates, string logMessage)
        {
            Action<EventMetadata, string> loggingFunction;
            int numberOfCertificates = certificates.Count();

            switch (numberOfCertificates)
            {
                case 0:
                    loggingFunction = tracer.RelatedError;
                    break;
                case 1:
                    loggingFunction = tracer.RelatedInfo;
                    break;
                default:
                    loggingFunction = tracer.RelatedWarning;
                    break;
            }

            loggingFunction(
                metadata,
                logMessage);
        }

        private static string GetSubjectNameLineForLogging(IEnumerable<X509Certificate2> certificates)
        {
            return string.Join(
                        Environment.NewLine,
                        certificates.Select(x => x.Subject));
        }

        private X509Certificate2 GetCertificateFromFile(ITracer tracer, EventMetadata metadata, GitProcess gitProcess)
        {
            string certificatePassword = null;
            if (this.isCertificatePasswordProtected)
            {
                certificatePassword = this.GetCertificatePassword(tracer, gitProcess);

                if (string.IsNullOrEmpty(certificatePassword))
                {
                    tracer.RelatedWarning(
                        new EventMetadata
                        {
                                { "SslCertificate", this.certificatePathOrSubjectCommonName }
                        },
                        "Git config indicates, that certificate is password protected, but retrieved password was null or empty!");
                }

                metadata.Add("isPasswordSpecified", string.IsNullOrEmpty(certificatePassword));
            }

            if (File.Exists(this.certificatePathOrSubjectCommonName))
            {
                try
                {
                    X509Certificate2 cert = new X509Certificate2(this.certificatePathOrSubjectCommonName, certificatePassword);
                    if (this.ShouldVerify && cert != null && !cert.Verify())
                    {
                        tracer.RelatedWarning(metadata, "Certficate was found, but is invalid.");
                        return null;
                    }

                    return cert;
                }
                catch (CryptographicException cryptEx)
                {
                    metadata.Add("Exception", cryptEx);
                    tracer.RelatedError(metadata, "Error, while loading certificate from disk");
                    return null;
                }
            }

            return null;
        }

        private X509Certificate2 GetCertificateFromStore(ITracer tracer, EventMetadata metadata)
        {
            try
            {
                using (X509Store store = new X509Store())
                {
                    store.Open(OpenFlags.OpenExistingOnly | OpenFlags.ReadOnly);

                    X509Certificate2Collection findResults = store.Certificates.Find(X509FindType.FindBySubjectName, this.certificatePathOrSubjectCommonName, this.ShouldVerify);
                    if (findResults?.Count > 0)
                    {
                        LogWithAppropriateLevel(
                            tracer,
                            metadata,
                            findResults.OfType<X509Certificate2>(),
                            string.Format(
                                "Found {0} certificates by provided name. Matching DNs: {1}",
                                findResults.Count,
                                GetSubjectNameLineForLogging(findResults.OfType<X509Certificate2>())));

                        X509Certificate2[] certsWithMatchingCns = findResults
                            .OfType<X509Certificate2>()
                            .Where(x => x.HasPrivateKey && Regex.IsMatch(x.Subject, string.Format("(^|,\\s?)CN={0}(,|$)", this.certificatePathOrSubjectCommonName))) // We only want certificates, that have private keys, as we need them. We also want a complete CN match
                            .OrderByDescending(x => x.Verify()) // Ordering by validity in a descending order will bring valid certificates to the beginning
                            .ThenBy(x => x.NotBefore) // We take the one, that was issued earliest, first
                            .ThenByDescending(x => x.NotAfter) // We then take the one, that is valid for the longest period
                            .ToArray();

                        LogWithAppropriateLevel(
                            tracer,
                            metadata,
                            certsWithMatchingCns,
                            string.Format(
                                "Found {0} certificates with a private key and an exact CN match. DNs (sorted by priority, will take first): {1}",
                                certsWithMatchingCns.Length,
                                GetSubjectNameLineForLogging(certsWithMatchingCns)));

                        return certsWithMatchingCns.FirstOrDefault();
                    }
                }
            }
            catch (CryptographicException cryptEx)
            {
                metadata.Add("Exception", cryptEx.ToString());
                tracer.RelatedError(metadata, "Error, while searching for certificate in store");
                return null;
            }

            return null;
        }
    }
}
