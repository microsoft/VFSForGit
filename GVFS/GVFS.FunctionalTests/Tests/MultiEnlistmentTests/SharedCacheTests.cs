using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.FunctionalTests.Tests.MultiEnlistmentTests
{
    [TestFixture]
    [Ignore("TODO 1081003: Re-enable shared cache")]
    public class SharedCacheTests
    {
        private const string WellKnownFile = "Readme.md";

        // This branch and commit sha should point to the same place.
        private const string WellKnownBranch = "FunctionalTests/20170602";
        private const string WellKnownCommitSha = "b407df4e21261e2bf022ef7031fabcf21ee0e14d";

        private List<GVFSFunctionalTestEnlistment> enlistmentsToDelete = new List<GVFSFunctionalTestEnlistment>();
        private string pathToObjectCache;
        private string localGvfsRoot;

        [SetUp]
        public void SetCacheLocation()
        {
            this.localGvfsRoot = Path.Combine(Properties.Settings.Default.EnlistmentRoot, "..", Guid.NewGuid().ToString("N"));
            this.pathToObjectCache = Path.Combine(this.localGvfsRoot, ".gvfsCache", "gitObjects");
        }

        [TearDown]
        public void DeleteEnlistmentsAndCache()
        {
            foreach (GVFSFunctionalTestEnlistment enlistment in this.enlistmentsToDelete)
            {
                enlistment.UnmountAndDeleteAll();
            }

            this.enlistmentsToDelete.Clear();

            this.DeleteSharedCache();
        }

        [TestCase]
        public void SecondCloneDoesNotDownloadAdditionalObjects()
        {
            GVFSFunctionalTestEnlistment enlistment1 = this.CreateNewEnlistment();
            File.ReadAllText(Path.Combine(enlistment1.RepoRoot, WellKnownFile));
            
            string[] allObjects = Directory.EnumerateFiles(enlistment1.ObjectRoot, "*", SearchOption.AllDirectories).ToArray();

            GVFSFunctionalTestEnlistment enlistment2 = this.CreateNewEnlistment();
            File.ReadAllText(Path.Combine(enlistment2.RepoRoot, WellKnownFile));

            enlistment2.ObjectRoot.ShouldEqual(enlistment1.ObjectRoot, "Sanity: Object roots are expected to match.");
            Directory.EnumerateFiles(enlistment2.ObjectRoot, "*", SearchOption.AllDirectories)
                .ShouldMatchInOrder(allObjects);
        }
        
        [TestCase]
        public void ParallelReadsInASharedCache()
        {
            GVFSFunctionalTestEnlistment enlistment1 = this.CreateNewEnlistment();
            GVFSFunctionalTestEnlistment enlistment2 = this.CreateNewEnlistment();
            GVFSFunctionalTestEnlistment enlistment3 = null;

            Task task1 = Task.Run(() => this.HydrateEntireRepo(enlistment1));
            Task task2 = Task.Run(() => this.HydrateEntireRepo(enlistment2));
            Task task3 = Task.Run(() => enlistment3 = this.CreateNewEnlistment());

            task1.Wait();
            task2.Wait();
            task3.Wait();

            enlistment1.Status().ShouldContain("Mount status: Ready");
            enlistment2.Status().ShouldContain("Mount status: Ready");
            enlistment3.Status().ShouldContain("Mount status: Ready");
        }

        [TestCase]
        public void DeleteCacheBeforeMount()
        {
            GVFSFunctionalTestEnlistment enlistment1 = this.CreateNewEnlistment();
            GVFSFunctionalTestEnlistment enlistment2 = this.CreateNewEnlistment();

            enlistment1.UnmountGVFS();

            this.DeleteSharedCache();

            enlistment1.MountGVFS();

            Task task1 = Task.Run(() => this.HydrateEntireRepo(enlistment1));
            Task task2 = Task.Run(() => this.HydrateEntireRepo(enlistment2));
            task1.Wait();
            task2.Wait();

            enlistment1.Status().ShouldContain("Mount status: Ready");
            enlistment2.Status().ShouldContain("Mount status: Ready");
        }

        [TestCase]
        public void DeleteCacheAtOffsetBetweenHydrations()
        {
            GVFSFunctionalTestEnlistment enlistment1 = this.CreateNewEnlistment();

            Task task1 = Task.Run(() =>
            {
                this.HydrateEntireRepo(enlistment1);
            });

            while (!task1.IsCompleted)
            {
                try
                {
                    this.DeleteSharedCache();
                    Thread.Sleep(100);
                }
                catch (IOException)
                {
                    // Hydration may have handles into the cache, so failing this delete is expected.
                }
            }

            enlistment1.Status().ShouldContain("Mount status: Ready");
        }

        [TestCase]
        public void DownloadingACommitWithoutTreesDoesntBreakNextClone()
        {
            GVFSFunctionalTestEnlistment enlistment1 = this.CreateNewEnlistment();
            GitProcess.Invoke(enlistment1.RepoRoot, "cat-file -s " + WellKnownCommitSha).ShouldEqual("293\n");

            GVFSFunctionalTestEnlistment enlistment2 = this.CreateNewEnlistment(WellKnownBranch);
            enlistment2.Status().ShouldContain("Mount status: Ready");
        }

        private void HydrateEntireRepo(GVFSFunctionalTestEnlistment enlistment)
        {
            List<string> allFiles = Directory.EnumerateFiles(enlistment.RepoRoot, "*", SearchOption.AllDirectories).ToList();
            for (int i = 0; i < allFiles.Count; ++i)
            {
                File.ReadAllText(allFiles[i]);
            }
        }

        private GVFSFunctionalTestEnlistment CreateNewEnlistment(string branch = null)
        {
            string pathToGvfs = Path.Combine(TestContext.CurrentContext.TestDirectory, Properties.Settings.Default.PathToGVFS);

            // TODO 1081003: Re-enable shared cache 
            // GVFSFunctionalTestEnlistment output = GVFSFunctionalTestEnlistment.CloneAndMount(pathToGvfs, commitish: branch, objectCachePath: this.pathToObjectCache);
            GVFSFunctionalTestEnlistment output = null;

            this.enlistmentsToDelete.Add(output);
            return output;
        }

        private void DeleteSharedCache()
        {
            CmdRunner.DeleteDirectoryWithRetry(this.localGvfsRoot);
        }
    }
}
