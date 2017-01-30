using GVFS.Common;
using GVFS.Common.Physical.Git;
using GVFS.Common.Tracing;
using System;

namespace GVFS.UnitTests.Mock.Physical.Git
{
    public class MockGitIndex : GitIndex
    {
        public MockGitIndex(ITracer tracer, Enlistment enlistment, string physicalIndexPath)
            : base(tracer, enlistment, physicalIndexPath, physicalIndexPath + ".lock")
        {
        }

        public override CallbackResult ClearSkipWorktreeAndUpdateEntry(string filePath, DateTime createTimeUtc, DateTime lastWriteTimeUtc, uint fileSize)
        {
            return CallbackResult.Success;
        }
    }
}
