using System;
using System.Collections.Generic;
using System.Text;

namespace GVFS.Common
{
    public interface IFileBasedLock : IDisposable
    {
        bool TryAcquireLock();
    }
}
