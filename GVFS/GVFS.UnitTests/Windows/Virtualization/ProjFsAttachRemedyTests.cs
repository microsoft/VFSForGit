using GVFS.Platform.Windows;
using GVFS.Tests.Should;
using NUnit.Framework;

namespace GVFS.UnitTests.Windows.Virtualization
{
    [TestFixture]
    public class ProjFsAttachRemedyTests
    {
        [TestCase]
        public void NamesTheVolumeTheEnlistmentIsOn()
        {
            // Only the drive root is read from the path. Nothing touches the file system.
            string remedy = WindowsFileSystemVirtualizer.BuildProjFsAttachRemedy("X:\\mock\\enlistment");

            remedy.ShouldContain("fsutil devdrv query X:");
            remedy.ShouldContain("setFiltersAllowed /f /volume X:");
            remedy.ShouldContain("If X: is a Dev Drive");
        }

        [TestCase]
        public void TellsTheUserToKeepTheFiltersThatAreAlreadyAllowed()
        {
            // setFiltersAllowed replaces the list, so advising only "PrjFlt" would drop
            // filters the machine already requires.
            string remedy = WindowsFileSystemVirtualizer.BuildProjFsAttachRemedy("X:\\mock\\enlistment");

            remedy.ShouldContain("PrjFlt");
            remedy.ShouldContain("<filters listed above>,PrjFlt");
            remedy.ShouldContain("replaces the list");
        }

        [TestCase]
        public void FallsBackToAPlaceholderWhenThereIsNoVolumeToName()
        {
            foreach (string enlistmentRoot in new string[] { null, string.Empty })
            {
                string remedy = WindowsFileSystemVirtualizer.BuildProjFsAttachRemedy(enlistmentRoot);

                remedy.ShouldContain("fsutil devdrv query <volume>");
                remedy.ShouldContain("setFiltersAllowed /f /volume <volume>");
            }
        }
    }
}
