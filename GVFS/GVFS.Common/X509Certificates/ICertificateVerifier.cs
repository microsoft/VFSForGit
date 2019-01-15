using System.Security.Cryptography.X509Certificates;

namespace GVFS.Common.X509Certificates
{
    public interface ICertificateVerifier
    {
        bool Verify(X509Certificate2 certificate);
    }
}
