using System.Collections.Generic;
using System.Linq;

namespace GVFS.Common.Git
{
    public class GitSslSettings
    {
        public readonly string SslCertificate;
        public readonly bool SslCertPasswordProtected;
        public readonly bool SslVerify;

        public GitSslSettings()
        {
            this.SslCertificate = null;
            this.SslCertPasswordProtected = false;
            this.SslVerify = true;
        }

        public GitSslSettings(IDictionary<string, GitConfigSetting> configSettings) : this()
        {
            if (configSettings != null)
            {
                if (configSettings.TryGetValue(GitConfigSetting.SslCert, out var sslCerts))
                {
                    this.SslCertificate = sslCerts.Values.Single();
                }

                if (configSettings.TryGetValue(GitConfigSetting.SslCertPasswordProtected, out var isSslCertPasswordProtected))
                {
                    this.SslCertPasswordProtected = isSslCertPasswordProtected.Values.Select(bool.Parse).Single();
                }

                if (configSettings.TryGetValue(GitConfigSetting.SslVerify, out var sslVerify))
                {
                    this.SslVerify = sslVerify.Values.Select(bool.Parse).Single();
                }
            }
        }
    }
}