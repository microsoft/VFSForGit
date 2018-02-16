using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
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
    public class SharedCacheTests : TestsWithMultiEnlistment
    {
        private const string WellKnownFile = "Readme.md";

        // This branch and commit sha should point to the same place.
        private const string WellKnownBranch = "FunctionalTests/20170602";
        private const string WellKnownCommitSha = "b407df4e21261e2bf022ef7031fabcf21ee0e14d";

        private string localCachePath;
        private string localCacheParentPath;

        private FileSystemRunner fileSystem;

        public SharedCacheTests()
        {
            this.fileSystem = new SystemIORunner();
        }

        [SetUp]
        public void SetCacheLocation()
        {
            this.localCacheParentPath = Path.Combine(Properties.Settings.Default.EnlistmentRoot, "..", Guid.NewGuid().ToString("N"));
            this.localCachePath = Path.Combine(this.localCacheParentPath, ".customGVFSCache");
        }

        [TearDown]
        public void DeleteCache()
        {
            CmdRunner.DeleteDirectoryWithRetry(this.localCacheParentPath);
        }

        [TestCase]
        public void SecondCloneDoesNotDownloadAdditionalObjects()
        {
            GVFSFunctionalTestEnlistment enlistment1 = this.CloneAndMountEnlistment();
            File.ReadAllText(Path.Combine(enlistment1.RepoRoot, WellKnownFile));

            this.AlternatesFileShouldHaveGitObjectsRoot(enlistment1);

            string[] allObjects = Directory.EnumerateFiles(enlistment1.LocalCacheRoot, "*", SearchOption.AllDirectories).ToArray();

            GVFSFunctionalTestEnlistment enlistment2 = this.CloneAndMountEnlistment();
            File.ReadAllText(Path.Combine(enlistment2.RepoRoot, WellKnownFile));

            this.AlternatesFileShouldHaveGitObjectsRoot(enlistment2);

            enlistment2.LocalCacheRoot.ShouldEqual(enlistment1.LocalCacheRoot, "Sanity: Local cache roots are expected to match.");
            Directory.EnumerateFiles(enlistment2.LocalCacheRoot, "*", SearchOption.AllDirectories)
                .ShouldMatchInOrder(allObjects);
        }

        [TestCase]
        public void CloneCleansUpStaleMetadataLock()
        {
            GVFSFunctionalTestEnlistment enlistment1 = this.CloneAndMountEnlistment();
            string metadataLockPath = Path.Combine(this.localCachePath, "mapping.dat.lock");
            metadataLockPath.ShouldNotExistOnDisk(this.fileSystem);
            this.fileSystem.WriteAllText(metadataLockPath, enlistment1.EnlistmentRoot);
            metadataLockPath.ShouldBeAFile(this.fileSystem);

            GVFSFunctionalTestEnlistment enlistment2 = this.CloneAndMountEnlistment();
            metadataLockPath.ShouldNotExistOnDisk(this.fileSystem);

            enlistment1.Status().ShouldContain("Mount status: Ready");
            enlistment2.Status().ShouldContain("Mount status: Ready");
        }
        
        [TestCase]
        public void ParallelReadsInASharedCache()
        {
            GVFSFunctionalTestEnlistment enlistment1 = this.CloneAndMountEnlistment();
            GVFSFunctionalTestEnlistment enlistment2 = this.CloneAndMountEnlistment();
            GVFSFunctionalTestEnlistment enlistment3 = null;

            Task task1 = Task.Run(() => this.HydrateEntireRepo(enlistment1));
            Task task2 = Task.Run(() => this.HydrateEntireRepo(enlistment2));
            Task task3 = Task.Run(() => enlistment3 = this.CloneAndMountEnlistment());

            task1.Wait();
            task2.Wait();
            task3.Wait();

            task1.Exception.ShouldBeNull();
            task2.Exception.ShouldBeNull();
            task3.Exception.ShouldBeNull();

            enlistment1.Status().ShouldContain("Mount status: Ready");
            enlistment2.Status().ShouldContain("Mount status: Ready");
            enlistment3.Status().ShouldContain("Mount status: Ready");

            this.AlternatesFileShouldHaveGitObjectsRoot(enlistment1);
            this.AlternatesFileShouldHaveGitObjectsRoot(enlistment2);
            this.AlternatesFileShouldHaveGitObjectsRoot(enlistment3);
        }

        [TestCase]
        public void DeleteCacheBeforeMount()
        {
            GVFSFunctionalTestEnlistment enlistment1 = this.CloneAndMountEnlistment();
            GVFSFunctionalTestEnlistment enlistment2 = this.CloneAndMountEnlistment();

            enlistment1.UnmountGVFS();

            CmdRunner.DeleteDirectoryWithRetry(this.localCachePath);

            enlistment1.MountGVFS();

            Task task1 = Task.Run(() => this.HydrateRootFolder(enlistment1));
            Task task2 = Task.Run(() => this.HydrateRootFolder(enlistment2));
            task1.Wait();
            task2.Wait();
            task1.Exception.ShouldBeNull();
            task2.Exception.ShouldBeNull();

            enlistment1.Status().ShouldContain("Mount status: Ready");
            enlistment2.Status().ShouldContain("Mount status: Ready");

            this.AlternatesFileShouldHaveGitObjectsRoot(enlistment1);
            this.AlternatesFileShouldHaveGitObjectsRoot(enlistment2);
        }

        [TestCase]
        public void DeleteCacheDuringHydrations()
        {
            GVFSFunctionalTestEnlistment enlistment1 = this.CloneAndMountEnlistment();

            Task task1 = Task.Run(() =>
            {
                this.HydrateEntireRepo(enlistment1);
            });

            while (!task1.IsCompleted)
            {
                try
                {
                    CmdRunner.DeleteDirectoryWithRetry(this.localCachePath);
                    Thread.Sleep(100);
                }
                catch (IOException)
                {
                    // Hydration may have handles into the cache, so failing this delete is expected.
                }
            }

            task1.Exception.ShouldBeNull();

            enlistment1.Status().ShouldContain("Mount status: Ready");
        }

        [TestCase]
        public void DownloadingACommitWithoutTreesDoesntBreakNextClone()
        {
            GVFSFunctionalTestEnlistment enlistment1 = this.CloneAndMountEnlistment();
            GitProcess.Invoke(enlistment1.RepoRoot, "cat-file -s " + WellKnownCommitSha).ShouldEqual("293\n");

            GVFSFunctionalTestEnlistment enlistment2 = this.CloneAndMountEnlistment(WellKnownBranch);
            enlistment2.Status().ShouldContain("Mount status: Ready");
        }

        [TestCase]
        public void MountReusesLocalCacheKeyWhenGitObjectsRootDeleted()
        {
            GVFSFunctionalTestEnlistment enlistment = this.CloneAndMountEnlistment();

            enlistment.UnmountGVFS();

            // Find the current git objects root and ensure it's on disk
            string objectsRoot = GVFSHelpers.GetPersistedGitObjectsRoot(enlistment.DotGVFSRoot).ShouldNotBeNull();
            objectsRoot.ShouldBeADirectory(this.fileSystem);

            string mappingFilePath = Path.Combine(enlistment.LocalCacheRoot, "mapping.dat");
            string mappingFileContents = this.fileSystem.ReadAllText(mappingFilePath);
            mappingFileContents.Length.ShouldNotEqual(0, "mapping.dat should not be empty");

            // Delete the git objects root folder, mount should re-create it and the mapping.dat file should not change
            CmdRunner.DeleteDirectoryWithRetry(objectsRoot);

            enlistment.MountGVFS();

            GVFSHelpers.GetPersistedGitObjectsRoot(enlistment.DotGVFSRoot).ShouldEqual(objectsRoot);
            objectsRoot.ShouldBeADirectory(this.fileSystem);
            mappingFilePath.ShouldBeAFile(this.fileSystem).WithContents(mappingFileContents);

            this.AlternatesFileShouldHaveGitObjectsRoot(enlistment);
        }

        [TestCase]
        public void MountUsesNewLocalCacheKeyWhenLocalCacheDeleted()
        {
            GVFSFunctionalTestEnlistment enlistment = this.CloneAndMountEnlistment();

            enlistment.UnmountGVFS();

            // Find the current git objects root and ensure it's on disk
            string objectsRoot = GVFSHelpers.GetPersistedGitObjectsRoot(enlistment.DotGVFSRoot).ShouldNotBeNull();
            objectsRoot.ShouldBeADirectory(this.fileSystem);

            string mappingFilePath = Path.Combine(enlistment.LocalCacheRoot, "mapping.dat");
            string mappingFileContents = this.fileSystem.ReadAllText(mappingFilePath);
            mappingFileContents.Length.ShouldNotEqual(0, "mapping.dat should not be empty");

            // Delete the local cache folder, mount should re-create it and generate a new mapping file and local cache key
            CmdRunner.DeleteDirectoryWithRetry(enlistment.LocalCacheRoot);

            enlistment.MountGVFS();

            // Mount should recreate the local cache root
            enlistment.LocalCacheRoot.ShouldBeADirectory(this.fileSystem);

            // Determine the new local cache key
            string newMappingFileContents = mappingFilePath.ShouldBeAFile(this.fileSystem).WithContents();
            const int GuidStringLength = 32;
            string mappingFileKey = "A {\"Key\":\"https://mseng.visualstudio.com/vsonline/_git/gvfs\",\"Value\":\"";
            int localKeyIndex = newMappingFileContents.IndexOf(mappingFileKey);
            string newCacheKey = newMappingFileContents.Substring(localKeyIndex + mappingFileKey.Length, GuidStringLength);

            // Validate the new objects root is on disk and uses the new key
            objectsRoot.ShouldNotExistOnDisk(this.fileSystem);
            string newObjectsRoot = GVFSHelpers.GetPersistedGitObjectsRoot(enlistment.DotGVFSRoot);
            newObjectsRoot.ShouldNotEqual(objectsRoot);
            newObjectsRoot.ShouldContain(newCacheKey);
            newObjectsRoot.ShouldBeADirectory(this.fileSystem);

            this.AlternatesFileShouldHaveGitObjectsRoot(enlistment);            
        }

        private GVFSFunctionalTestEnlistment CloneAndMountEnlistment(string branch = null)
        {
            return this.CreateNewEnlistment(this.localCachePath, branch);
        }

        private void AlternatesFileShouldHaveGitObjectsRoot(GVFSFunctionalTestEnlistment enlistment)
        {
            string objectsRoot = GVFSHelpers.GetPersistedGitObjectsRoot(enlistment.DotGVFSRoot);
            string alternatesFileContents = Path.Combine(enlistment.RepoRoot, ".git", "objects", "info", "alternates").ShouldBeAFile(this.fileSystem).WithContents();
            alternatesFileContents.ShouldEqual(objectsRoot);
        }

        private void HydrateRootFolder(GVFSFunctionalTestEnlistment enlistment)
        {
            List<string> allFiles = Directory.EnumerateFiles(enlistment.RepoRoot, "*", SearchOption.TopDirectoryOnly).ToList();
            for (int i = 0; i < allFiles.Count; ++i)
            {
                File.ReadAllText(allFiles[i]);
            }
        }

        private void HydrateEntireRepo(GVFSFunctionalTestEnlistment enlistment)
        {
            List<string> allFiles = Directory.EnumerateFiles(enlistment.RepoRoot, "*", SearchOption.AllDirectories).ToList();
            for (int i = 0; i < allFiles.Count; ++i)
            {
                if (!allFiles[i].StartsWith(enlistment.RepoRoot + "\\.git\\", StringComparison.OrdinalIgnoreCase))
                {
                    File.ReadAllText(allFiles[i]);
                }
            }
        }
    }
}
