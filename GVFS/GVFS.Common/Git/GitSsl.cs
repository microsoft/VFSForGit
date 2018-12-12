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
    public class GitSsl : IDisposable
    {
        public readonly string CertificatePathOrSubjectCommonName;
        public readonly bool IsCertificatePasswordProtected;
        public readonly bool ShouldVerify;

        private readonly Lazy<X509Store> store = new Lazy<X509Store>(() =>
        {
            X509Store s = new X509Store();
            s.Open(OpenFlags.OpenExistingOnly | OpenFlags.ReadOnly);
            return s;
        });

        public GitSsl()
        {
            this.CertificatePathOrSubjectCommonName = null;
            this.IsCertificatePasswordProtected = false;
            this.ShouldVerify = true;
        }

        public GitSsl(IDictionary<string, GitConfigSetting> configSettings) : this()
        {
            if (configSettings != null)
            {
                if (configSettings.TryGetValue(GitConfigSetting.HttpSslCert, out GitConfigSetting sslCerts))
                {
                    this.CertificatePathOrSubjectCommonName = sslCerts.Values.Single();
                }

                if (configSettings.TryGetValue(GitConfigSetting.HttpSslCertPasswordProtected, out GitConfigSetting isSslCertPasswordProtected))
                {
                    this.IsCertificatePasswordProtected = isSslCertPasswordProtected.Values.Select(bool.Parse).Single();
                }

                if (configSettings.TryGetValue(GitConfigSetting.HttpSslVerify, out GitConfigSetting sslVerify))
                {
                    this.ShouldVerify = sslVerify.Values.Select(bool.Parse).Single();
                }
            }
        }

        public string GetCertificatePassword(ITracer tracer, GitProcess git)
        {
            if (git.TryGetCertificatePassword(tracer, this.CertificatePathOrSubjectCommonName, out string password, out string error))
            {
                return password;
            }

            return null;
        }

        public X509Certificate2 GetCertificate(ITracer tracer, string certificatePassword, bool onlyLoadValidCertificateFromStore)
        {
            EventMetadata metadata = new EventMetadata
            {
                { "CertificatePathOrSubjectCommonName", this.CertificatePathOrSubjectCommonName },
                { "isPasswordSpecified", string.IsNullOrEmpty(certificatePassword) },
                { "shouldVerify", onlyLoadValidCertificateFromStore }
            };

            var result =
                this.GetCertificateFromFile(tracer, metadata, certificatePassword, onlyLoadValidCertificateFromStore) ??
                this.GetCertificateFromStore(tracer, metadata, onlyLoadValidCertificateFromStore);

            if (result == null)
            {
                tracer.RelatedError("Certificate {0} not found", this.CertificatePathOrSubjectCommonName);
            }

            return result;
        }

        private X509Certificate2 GetCertificateFromFile(ITracer tracer, EventMetadata metadata, string certificatePassword, bool onlyLoadValidCertificateFromStore)
        {
            if (File.Exists(this.CertificatePathOrSubjectCommonName))
            {
                try
                {
                    X509Certificate2 cert = new X509Certificate2(this.CertificatePathOrSubjectCommonName, certificatePassword);
                    if (onlyLoadValidCertificateFromStore && cert != null && !cert.Verify())
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

        private X509Certificate2 GetCertificateFromStore(ITracer tracer, EventMetadata metadata, bool onlyLoadValidCertificateFromStore)
        {
            try
            {
                X509Certificate2Collection findResults = this.store.Value.Certificates.Find(X509FindType.FindBySubjectName, this.CertificatePathOrSubjectCommonName, onlyLoadValidCertificateFromStore);
                if (findResults?.Count > 0)
                {
                    LogCertificateCounts(tracer, metadata, findResults.OfType<X509Certificate2>(), "Found {0} certificates by provided name. Matching DNs: {1}");

                    X509Certificate2[] certsWithMatchingCns = findResults
                        .OfType<X509Certificate2>()
                        .Where(x => x.HasPrivateKey && Regex.IsMatch(x.Subject, string.Format("(^|,\\s?)CN={0}(,|$)", this.CertificatePathOrSubjectCommonName))) // We only want certificates, that have private keys, as we need them. We also want a complete CN match
                        .OrderByDescending(x => x.Verify()) // Ordering by validity in a descending order will bring valid certificates to the beginning
                        .ThenBy(x => x.NotBefore) // We take the one, that was issued earliest, first
                        .ThenByDescending(x => x.NotAfter) // We then take the one, that is valid for the longest period
                        .ToArray();

                    LogCertificateCounts(tracer, metadata, certsWithMatchingCns, "Found {0} certificates with a private key and an exact CN match. DNs (sorted by priority, will take first): {1}");

                    return certsWithMatchingCns.FirstOrDefault();
                }
            }
            catch (CryptographicException cryptEx)
            {
                metadata.Add("Exception", cryptEx);
                tracer.RelatedError(metadata, "Error, while searching for certificate in store");
                return null;
            }

            return null;
        }

        private static void LogCertificateCounts(ITracer tracer, EventMetadata metadata, IEnumerable<X509Certificate2> certificates, string messageTemplate)
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
                string.Format(
                    messageTemplate,
                    numberOfCertificates,
                    string.Join(
                        Environment.NewLine,
                        certificates.Select(x => x.Subject))));
        }

        public void Dispose()
        {
            if (this.store.IsValueCreated)
            {
                this.store.Value.Dispose();
            }
        }
    }
}
