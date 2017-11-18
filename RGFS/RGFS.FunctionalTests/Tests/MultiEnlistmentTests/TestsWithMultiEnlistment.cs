using RGFS.FunctionalTests.Tools;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;

namespace RGFS.FunctionalTests.Tests.MultiEnlistmentTests
{
    public class TestsWithMultiEnlistment
    {
        private List<RGFSFunctionalTestEnlistment> enlistmentsToDelete = new List<RGFSFunctionalTestEnlistment>();

        [TearDown]
        public void DeleteEnlistments()
        {
            foreach (RGFSFunctionalTestEnlistment enlistment in this.enlistmentsToDelete)
            {
                enlistment.UnmountAndDeleteAll();
            }

            this.enlistmentsToDelete.Clear();
        }

        protected RGFSFunctionalTestEnlistment CreateNewEnlistment(string objectCachePath = null, string branch = null)
        {
            string pathToRgfs = Path.Combine(TestContext.CurrentContext.TestDirectory, Properties.Settings.Default.PathToRGFS);

            // TODO 1081003: Re-enable shared cache - use objectCachePath argument
            RGFSFunctionalTestEnlistment output = RGFSFunctionalTestEnlistment.CloneAndMount(pathToRgfs, branch);
            this.enlistmentsToDelete.Add(output);
            return output;
        }
    }
}
