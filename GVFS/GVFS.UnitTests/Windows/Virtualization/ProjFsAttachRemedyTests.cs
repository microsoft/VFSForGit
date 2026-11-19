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
            string remedy = ProjFsAttachRemedy.BuildForEnlistment("X:\\mock\\enlistment");

            remedy.ShouldContain("fsutil devdrv query X:");
            remedy.ShouldContain("setFiltersAllowed /volume X:");
            remedy.ShouldContain("If X: is a Dev Drive");
        }

        [TestCase]
        public void TellsTheUserToKeepTheFiltersThatAreAlreadyAllowed()
        {
            // setFiltersAllowed replaces the list, so advising only "PrjFlt" would drop
            // filters the machine already requires.
            string remedy = ProjFsAttachRemedy.BuildForEnlistment("X:\\mock\\enlistment");

            remedy.ShouldContain("PrjFlt");
            remedy.ShouldContain("<existing volume filters>,PrjFlt");
            remedy.ShouldContain("replaces the volume list");
        }

        [TestCase]
        public void FallsBackToAPlaceholderWhenThereIsNoVolumeToName()
        {
            foreach (string enlistmentRoot in new string[] { null, string.Empty })
            {
                string remedy = ProjFsAttachRemedy.BuildForEnlistment(enlistmentRoot);

                remedy.ShouldContain("fsutil devdrv query <volume>");
                remedy.ShouldContain("setFiltersAllowed /volume <volume>");
            }
        }

        [TestCase]
        public void VolumeRemedyDoesNotForceDismountAndExplainsTheSavedVolumeList()
        {
            string remedy = ProjFsAttachRemedy.BuildForVolume(@"X:\");
            remedy.ShouldContain("Filters allowed on this developer volume", "keep its existing entries");
            remedy.ShouldContain("If that section is absent, use \"PrjFlt\"", "remount it, or restart the computer");
            remedy.ShouldNotContain(false, "setFiltersAllowed /f");
        }

        [TestCase]
        public void VolumeRemedyPreservesAndQuotesAFolderMountPoint()
        {
            string remedy = ProjFsAttachRemedy.BuildForVolume("X:\\mock volume\\");
            remedy.ShouldContain("fsutil devdrv query \"X:\\mock volume\\\\\"");
            remedy.ShouldContain("setFiltersAllowed /volume \"X:\\mock volume\\\\\"");
        }
    }
}
