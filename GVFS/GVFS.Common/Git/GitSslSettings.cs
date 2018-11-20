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
            this.SslCertificate = null;
            this.SslCertPasswordProtected = false;

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
            }
        }
    }
}