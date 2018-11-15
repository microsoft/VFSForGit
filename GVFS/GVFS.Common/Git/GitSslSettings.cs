using System.Collections.Generic;
using System.Linq;

namespace GVFS.Common.Git
{
    public struct GitSslSettings
    {
        public readonly string SslCertificate;
        public readonly bool SslCertPasswordProtected;

        public GitSslSettings(IDictionary<string, GitConfigSetting> configSettings)
        {
            if (configSettings != null)
            {
                if (configSettings.TryGetValue(GitConfigSetting.SslCert, out var sslCerts))
                {
                    this.SslCertificate = sslCerts.Values.Single();
                }
                else
                {
                    this.SslCertificate = default(string);
                }

                if (configSettings.TryGetValue(GitConfigSetting.SslCertPasswordProtected, out var isSslCertPasswordProtected))
                {
                    this.SslCertPasswordProtected = isSslCertPasswordProtected.Values.Select(bool.Parse).Single();
                }
                else
                {
                    this.SslCertPasswordProtected = false;
                }
            }
            else
            {
                this.SslCertificate = default(string);
                this.SslCertPasswordProtected = default(bool);
            }
        }
    }
}