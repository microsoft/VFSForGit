using GVFS.FunctionalTests.Tools;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;

namespace GVFS.FunctionalTests.Tests.MultiEnlistmentTests
{
    public class TestsWithMultiEnlistment
    {
        private List<GVFSFunctionalTestEnlistment> enlistmentsToDelete = new List<GVFSFunctionalTestEnlistment>();

        [TearDown]
        public void DeleteEnlistments()
        {
            foreach (GVFSFunctionalTestEnlistment enlistment in this.enlistmentsToDelete)
            {
                enlistment.UnmountAndDeleteAll();
            }

            this.enlistmentsToDelete.Clear();
        }

        protected GVFSFunctionalTestEnlistment CreateNewEnlistment(string localCacheRoot = null, string branch = null)
        {
            string pathToGvfs = Path.Combine(TestContext.CurrentContext.TestDirectory, Properties.Settings.Default.PathToGVFS);

            GVFSFunctionalTestEnlistment output = GVFSFunctionalTestEnlistment.CloneAndMount(pathToGvfs, branch, localCacheRoot);
            this.enlistmentsToDelete.Add(output);
            return output;
        }
    }
}
