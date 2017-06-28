using System;
using GVFS.Common;

namespace GVFS.UnitTests.Mock.Common
{
    public class MockEnlistment : Enlistment
    {
        public MockEnlistment()
            : base("mock:\\path", "mock:\\path", "mock:\\path", "mock:\\repoUrl", "mock:\\cacheUrl", "mock:\\git", null)
        {
        }

        public string SmudgedBlobsRoot { get; set; }
    }
}
