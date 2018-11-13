using System.Collections.Generic;
using System.Linq;

namespace GVFS.Common.Git
{
    public struct GitSslSettings
    {
        public GitSslSettings(IDictionary<string, GitConfigSetting> configSettings)
        {
            if (configSettings != null)
            {
                if (configSettings.TryGetValue(GitConfigSetting.SslCert, out var sslCerts))
                {
                    SslCertificate = sslCerts.Values.Single();
                }
                else
                {
                    SslCertificate = default(string);
                }

                if (configSettings.TryGetValue(GitConfigSetting.SslCertPasswordProtected, out var isSslCertPasswordProtected))
                {
                    SslCertPasswordProtected = isSslCertPasswordProtected.Values.Select(bool.Parse).Single();
                }
                else
                {
                    SslCertPasswordProtected = false;
                }
            }
            else
            {
                SslCertificate = default(string);
                SslCertPasswordProtected = default(bool);
            }
        }
        public readonly string SslCertificate;
        public readonly bool SslCertPasswordProtected;
    }
}