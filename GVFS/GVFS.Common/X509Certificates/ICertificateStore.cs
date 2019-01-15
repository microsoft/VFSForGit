using System;
using System.Security.Cryptography.X509Certificates;

namespace GVFS.Common.X509Certificates
{
    public interface ICertificateStore : IDisposable
    {
        X509Certificate2Collection Find(X509FindType findType, string searchString, bool validOnly);
    }
}
