using System;
using System.Security.Cryptography.X509Certificates;

namespace GVFS.Common.X509Certificates
{
    public class SystemCertificateStore : IDisposable
    {
        private readonly X509Store store;

        private bool isOpen = false;

        public SystemCertificateStore()
        {
            this.store = new X509Store();
        }

        public void Dispose()
        {
            this.store.Dispose();
        }

        public virtual X509Certificate2Collection Find(X509FindType findType, string searchString, bool validOnly)
        {
            if (!this.isOpen)
            {
                this.store.Open(OpenFlags.OpenExistingOnly | OpenFlags.ReadOnly);
                this.isOpen = true;
            }

            return this.store.Certificates.Find(X509FindType.FindBySubjectName, searchString, validOnly);
        }
    }
}
