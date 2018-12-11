using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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
                { "certId", this.CertificatePathOrSubjectCommonName },
                { "isPasswordSpecified", string.IsNullOrEmpty(certificatePassword) },
                { "shouldVerify", onlyLoadValidCertificateFromStore }
            };

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

            try
            {
                X509Certificate2Collection findResults = this.store.Value.Certificates.Find(X509FindType.FindBySubjectName, this.CertificatePathOrSubjectCommonName, onlyLoadValidCertificateFromStore);
                if (findResults?.Count > 0)
                {
                    return findResults[0];
                }
            }
            catch (CryptographicException cryptEx)
            {
                metadata.Add("Exception", cryptEx);
                tracer.RelatedError(metadata, "Error, while searching for certificate in store");
                return null;
            }

            tracer.RelatedError("Certificate {0} not found", this.CertificatePathOrSubjectCommonName);
            return null;
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
