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
        public readonly string SslCertificate;
        public readonly bool SslCertPasswordProtected;
        public readonly bool SslVerify;

        private readonly Lazy<X509Store> store = new Lazy<X509Store>(() =>
        {
            X509Store s = new X509Store();
            s.Open(OpenFlags.OpenExistingOnly | OpenFlags.ReadOnly);
            return s;
        });

        public GitSsl()
        {
            this.SslCertificate = null;
            this.SslCertPasswordProtected = false;
            this.SslVerify = true;
        }

        public GitSsl(IDictionary<string, GitConfigSetting> configSettings) : this()
        {
            if (configSettings != null)
            {
                if (configSettings.TryGetValue(GitConfigSetting.HttpSslCert, out GitConfigSetting sslCerts))
                {
                    this.SslCertificate = sslCerts.Values.Single();
                }

                if (configSettings.TryGetValue(GitConfigSetting.HttpSslCertPasswordProtected, out GitConfigSetting isSslCertPasswordProtected))
                {
                    this.SslCertPasswordProtected = isSslCertPasswordProtected.Values.Select(bool.Parse).Single();
                }

                if (configSettings.TryGetValue(GitConfigSetting.HttpSslVerify, out GitConfigSetting sslVerify))
                {
                    this.SslVerify = sslVerify.Values.Select(bool.Parse).Single();
                }
            }
        }

        public string GetCertificatePassword(ITracer tracer, GitProcess git)
        {
            if (git.TryGetCertificatePassword(tracer, this.SslCertificate, out string password, out string error))
            {
                return password;
            }

            return null;
        }

        public X509Certificate2 LoadCertificate(ITracer tracer, string certificatePassword, bool onlyLoadValidCertificateFromStore)
        {
            EventMetadata metadata = new EventMetadata
            {
                { "certId", this.SslCertificate },
                { "isPasswordSpecified", string.IsNullOrEmpty(certificatePassword) },
                { "shouldVerify", onlyLoadValidCertificateFromStore }
            };

            if (File.Exists(this.SslCertificate))
            {
                try
                {
                    X509Certificate2 cert = new X509Certificate2(this.SslCertificate, certificatePassword);
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
                X509Certificate2Collection findResults = this.store.Value.Certificates.Find(X509FindType.FindBySubjectName, this.SslCertificate, onlyLoadValidCertificateFromStore);
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

            tracer.RelatedError("Certificate {0} not found", this.SslCertificate);
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
