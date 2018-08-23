using GVFS.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace GVFS.UnitTests.Mock.Common
{
    public class MockFileBasedLock : IFileBasedLock
    {
        public MockFileBasedLock()
        {
        }

        public bool TryAcquireLock()
        {
            return true;
        }

        public void Dispose()
        {
        }
    }
}
