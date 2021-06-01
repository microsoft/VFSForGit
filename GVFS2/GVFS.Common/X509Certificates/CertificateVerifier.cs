using System.Security.Cryptography.X509Certificates;

namespace GVFS.Common.X509Certificates
{
    public class CertificateVerifier
    {
        public virtual bool Verify(X509Certificate2 certificate)
        {
            return certificate.Verify();
        }
    }
}
