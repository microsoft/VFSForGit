using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using GVFS.Common.X509Certificates;

namespace GVFS.Common.Git
{
    public class GitSsl
    {
        private readonly string certificatePathOrSubjectCommonName;
        private readonly bool isCertificatePasswordProtected;
        private readonly Func<SystemCertificateStore> createCertificateStore;
        private readonly CertificateVerifier certificateVerifier;
        private readonly PhysicalFileSystem fileSystem;

        public GitSsl(
            IDictionary<string, GitConfigSetting> configSettings,
            Func<SystemCertificateStore> createCertificateStore = null,
            CertificateVerifier certificateVerifier = null,
            PhysicalFileSystem fileSystem = null) : this(createCertificateStore, certificateVerifier, fileSystem)
        {
            if (configSettings != null)
            {
                if (configSettings.TryGetValue(GitConfigSetting.HttpSslCert, out GitConfigSetting sslCerts))
                {
                    this.certificatePathOrSubjectCommonName = sslCerts.Values.Last();
                }

                this.isCertificatePasswordProtected = SetBoolSettingOrThrow(configSettings, GitConfigSetting.HttpSslCertPasswordProtected, this.isCertificatePasswordProtected);

                this.ShouldVerify = SetBoolSettingOrThrow(configSettings, GitConfigSetting.HttpSslVerify, this.ShouldVerify);
            }
        }

        private GitSsl(Func<SystemCertificateStore> createCertificateStore, CertificateVerifier certificateVerifier, PhysicalFileSystem fileSystem)
        {
            this.fileSystem = fileSystem ?? new PhysicalFileSystem();

            this.createCertificateStore = createCertificateStore ?? (() => new SystemCertificateStore());

            this.certificateVerifier = certificateVerifier ?? new CertificateVerifier();

            this.certificatePathOrSubjectCommonName = null;

            this.isCertificatePasswordProtected = false;

            // True by default, both to have good default security settings and to match git behavior.
            // https://git-scm.com/docs/git-config#git-config-httpsslVerify
            this.ShouldVerify = true;
        }

        /// <summary>
        /// Gets a value indicating whether SSL certificates being loaded should be verified. Also used to determine, whether client should verify server SSL certificate. True by default.
        /// </summary>
        /// <value><c>true</c> if should verify SSL certificates; otherwise, <c>false</c>.</value>
        public bool ShouldVerify { get; }

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
                tracer.RelatedError(metadata, $"Certificate {this.certificatePathOrSubjectCommonName} not found");
            }

            return result;
        }

        private static bool SetBoolSettingOrThrow(IDictionary<string, GitConfigSetting> configSettings, string settingName, bool currentValue)
        {
            if (configSettings.TryGetValue(settingName, out GitConfigSetting settingValues))
            {
                try
                {
                    return bool.Parse(settingValues.Values.Last());
                }
                catch (FormatException)
                {
                    throw new InvalidRepoException($"{settingName} git setting did not have a bool-parsable value. Found: {string.Join(" ", settingValues.Values)}");
                }
            }

            return currentValue;
        }

        private static void LogWithAppropriateLevel(ITracer tracer, EventMetadata metadata, IEnumerable<X509Certificate2> certificates, string logMessage)
        {
            int numberOfCertificates = certificates.Count();

            switch (numberOfCertificates)
            {
                case 0:
                    tracer.RelatedError(metadata, logMessage);
                    break;
                case 1:
                    tracer.RelatedInfo(metadata, logMessage);
                    break;
                default:
                    tracer.RelatedWarning(metadata, logMessage);
                    break;
            }
        }

        private static string GetSubjectNameLineForLogging(IEnumerable<X509Certificate2> certificates)
        {
            return string.Join(
                        Environment.NewLine,
                        certificates.Select(x => x.Subject));
        }

        private string GetCertificatePassword(ITracer tracer, GitProcess git)
        {
            if (git.TryGetCertificatePassword(tracer, this.certificatePathOrSubjectCommonName, out string password, out string error))
            {
                return password;
            }

            return null;
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
                        metadata,
                        "Git config indicates, that certificate is password protected, but retrieved password was null or empty!");
                }

                metadata.Add("isPasswordSpecified", string.IsNullOrEmpty(certificatePassword));
            }

            if (this.fileSystem.FileExists(this.certificatePathOrSubjectCommonName))
            {
                try
                {
                    byte[] certificateContent = this.fileSystem.ReadAllBytes(this.certificatePathOrSubjectCommonName);
                    X509Certificate2 cert = new X509Certificate2(certificateContent, certificatePassword);
                    if (this.ShouldVerify && cert != null && !this.certificateVerifier.Verify(cert))
                    {
                        tracer.RelatedWarning(metadata, "Certficate was found, but is invalid.");
                        return null;
                    }

                    return cert;
                }
                catch (CryptographicException cryptEx)
                {
                    metadata.Add("Exception", cryptEx.ToString());
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
                using (SystemCertificateStore certificateStore = this.createCertificateStore())
                {
                    X509Certificate2Collection findResults = certificateStore.Find(
                        X509FindType.FindBySubjectName,
                        this.certificatePathOrSubjectCommonName,
                        this.ShouldVerify);

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
                            .OrderByDescending(x => this.certificateVerifier.Verify(x)) // Ordering by validity in a descending order will bring valid certificates to the beginning
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
