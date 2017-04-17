using GVFS.GVFlt;
using GVFS.Tests.Should;
using NUnit.Framework;

namespace GVFS.UnitTests.GVFlt.DotGit
{
    public class GVFltCallbacksTests
    {
        [TestCase]
        public void CannotDeleteIndexOrPacks()
        {
            GVFltCallbacks.DoesPathAllowDelete(string.Empty).ShouldEqual(true);

            GVFltCallbacks.DoesPathAllowDelete(@".git\index").ShouldEqual(false);
            GVFltCallbacks.DoesPathAllowDelete(@".git\INDEX").ShouldEqual(false);

            GVFltCallbacks.DoesPathAllowDelete(@".git\index.lock").ShouldEqual(true);
            GVFltCallbacks.DoesPathAllowDelete(@".git\INDEX.lock").ShouldEqual(true);
            GVFltCallbacks.DoesPathAllowDelete(@".git\objects\pack").ShouldEqual(true);
            GVFltCallbacks.DoesPathAllowDelete(@".git\objects\pack-temp").ShouldEqual(true);
            GVFltCallbacks.DoesPathAllowDelete(@".git\objects\pack\pack-1e88df2a4e234c82858cfe182070645fb96d6131.pack").ShouldEqual(true);
            GVFltCallbacks.DoesPathAllowDelete(@".git\objects\pack\pack-1e88df2a4e234c82858cfe182070645fb96d6131.idx").ShouldEqual(true);
        }

        [TestCase]
        public void IsPathMonitoredForWrites()
        {
            GVFltCallbacks.IsPathMonitoredForWrites(string.Empty).ShouldEqual(false);
            GVFltCallbacks.IsPathMonitoredForWrites(@".git\index").ShouldEqual(true);
            GVFltCallbacks.IsPathMonitoredForWrites(@".git\INDEX").ShouldEqual(true);
            GVFltCallbacks.IsPathMonitoredForWrites(@".git\index.lock").ShouldEqual(false);
            GVFltCallbacks.IsPathMonitoredForWrites(@".git\INDEX.lock").ShouldEqual(false);
            GVFltCallbacks.IsPathMonitoredForWrites(@".git\head").ShouldEqual(false);
            GVFltCallbacks.IsPathMonitoredForWrites(@".git\HEAD").ShouldEqual(false);
            GVFltCallbacks.IsPathMonitoredForWrites(@".git\logs\head").ShouldEqual(true);
            GVFltCallbacks.IsPathMonitoredForWrites(@".git\logs\HEAD").ShouldEqual(true);
            GVFltCallbacks.IsPathMonitoredForWrites(@".git\head.lock").ShouldEqual(false);
            GVFltCallbacks.IsPathMonitoredForWrites(@".git\HEAD.lock").ShouldEqual(false);
            GVFltCallbacks.IsPathMonitoredForWrites(@".git\refs\heads\master").ShouldEqual(false);
            GVFltCallbacks.IsPathMonitoredForWrites(@".git\refs\heads\users\testuser\feature_branch").ShouldEqual(false);
            GVFltCallbacks.IsPathMonitoredForWrites(@".git\refs\remotes").ShouldEqual(false);
            GVFltCallbacks.IsPathMonitoredForWrites(@".git\refs\remotes\master").ShouldEqual(false);
            GVFltCallbacks.IsPathMonitoredForWrites(@".git\objects\pack").ShouldEqual(false);
            GVFltCallbacks.IsPathMonitoredForWrites(@".git\objects").ShouldEqual(false);
        }
    }
}