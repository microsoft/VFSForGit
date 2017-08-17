using System;
using GVFS.Common;

namespace GVFS.UnitTests.Mock.Common
{
    public class MockEnlistment : Enlistment
    {
        public MockEnlistment()
            : base("mock:\\path", "mock:\\path", "mock:\\path\\.git\\objects", "mock:\\repoUrl", "mock:\\git", null)
        {
        }
    }
}
