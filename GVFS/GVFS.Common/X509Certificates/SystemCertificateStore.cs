using System.Security.Cryptography.X509Certificates;

namespace GVFS.Common.X509Certificates
{
    public class SystemCertificateStore : ICertificateStore
    {
        private readonly X509Store store;
        public SystemCertificateStore()
        {
            this.store = new X509Store();
        }

        public void Dispose()
        {
            this.store.Dispose();
        }

        public X509Certificate2Collection Find(X509FindType findType, string searchString, bool validOnly)
        {
            if (!this.store.IsOpen)
            {
                this.store.Open(OpenFlags.OpenExistingOnly | OpenFlags.ReadOnly);
            }

            return this.store.Certificates.Find(X509FindType.FindBySubjectName, searchString, validOnly);
        }
    }
}
