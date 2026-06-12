using GVFS.Common;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.FileSystem;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class LocalRepoRegistryTests
    {
        private const string DataLocation = @"mock:\registryDataFolder";
        private const string Repo1 = @"mock:\code\repo1";
        private const string Repo2 = @"mock:\code\repo2";
        private const string Repo3 = @"mock:\code\repo3";

        [TestCase]
        public void TryRegisterRepo_EmptyRegistry_RoundTripsThroughDisk()
        {
            (LocalRepoRegistry registry, MockFileSystem _) = this.CreateRegistry();
            string ownerSID = Guid.NewGuid().ToString();

            registry.TryRegisterRepo(Repo1, ownerSID, out string error).ShouldBeTrue(error);

            Dictionary<string, LocalRepoRegistration> all = registry.ReadRegistry();
            all.Count.ShouldEqual(1);
            VerifyEntry(all[Repo1], expectedOwnerSID: ownerSID, expectedIsActive: true);
        }

        [TestCase]
        public void TryRegisterRepo_DuplicateActiveSameOwner_DoesNotRewrite()
        {
            (LocalRepoRegistry registry, MockFileSystem fs) = this.CreateRegistry();
            string ownerSID = Guid.NewGuid().ToString();
            registry.TryRegisterRepo(Repo1, ownerSID, out _).ShouldBeTrue();

            string contentBefore = fs.ReadAllText(Path.Combine(DataLocation, LocalRepoRegistry.RegistryFileName));
            registry.TryRegisterRepo(Repo1, ownerSID, out _).ShouldBeTrue();
            string contentAfter = fs.ReadAllText(Path.Combine(DataLocation, LocalRepoRegistry.RegistryFileName));

            // No semantic change → no rewrite. Important for caller patterns
            // that re-register on every mount; we don't want a writer storm.
            contentAfter.ShouldEqual(contentBefore);
        }

        [TestCase]
        public void TryRegisterRepo_ReactivatesAfterDeactivate()
        {
            (LocalRepoRegistry registry, _) = this.CreateRegistry();
            string ownerSID = Guid.NewGuid().ToString();

            registry.TryRegisterRepo(Repo1, ownerSID, out _).ShouldBeTrue();
            registry.TryDeactivateRepo(Repo1, out _).ShouldBeTrue();
            registry.TryRegisterRepo(Repo1, ownerSID, out _).ShouldBeTrue();

            Dictionary<string, LocalRepoRegistration> all = registry.ReadRegistry();
            VerifyEntry(all[Repo1], expectedOwnerSID: ownerSID, expectedIsActive: true);
        }

        [TestCase]
        public void TryRegisterRepo_NewOwnerSidIsPersisted()
        {
            (LocalRepoRegistry registry, _) = this.CreateRegistry();
            string ownerA = Guid.NewGuid().ToString();
            string ownerB = Guid.NewGuid().ToString();

            registry.TryRegisterRepo(Repo1, ownerA, out _).ShouldBeTrue();
            registry.TryRegisterRepo(Repo1, ownerB, out _).ShouldBeTrue();

            Dictionary<string, LocalRepoRegistration> all = registry.ReadRegistry();
            VerifyEntry(all[Repo1], expectedOwnerSID: ownerB, expectedIsActive: true);
        }

        [TestCase]
        public void TryDeactivateRepo_NonExistent_ReturnsFalseWithError()
        {
            (LocalRepoRegistry registry, _) = this.CreateRegistry();

            registry.TryDeactivateRepo(Repo1, out string error).ShouldBeFalse();
            string.IsNullOrEmpty(error).ShouldBeFalse();
        }

        [TestCase]
        public void TryDeactivateRepo_AlreadyInactive_StillReturnsTrue()
        {
            (LocalRepoRegistry registry, _) = this.CreateRegistry();
            string ownerSID = Guid.NewGuid().ToString();

            registry.TryRegisterRepo(Repo1, ownerSID, out _).ShouldBeTrue();
            registry.TryDeactivateRepo(Repo1, out _).ShouldBeTrue();
            // Second deactivate on an already-inactive entry is a no-op success
            registry.TryDeactivateRepo(Repo1, out _).ShouldBeTrue();
        }

        [TestCase]
        public void TryRemoveRepo_RemovesEntryEntirely()
        {
            (LocalRepoRegistry registry, _) = this.CreateRegistry();
            string ownerSID = Guid.NewGuid().ToString();

            registry.TryRegisterRepo(Repo1, ownerSID, out _).ShouldBeTrue();
            registry.TryRemoveRepo(Repo1, out _).ShouldBeTrue();

            Dictionary<string, LocalRepoRegistration> all = registry.ReadRegistry();
            all.Count.ShouldEqual(0);
        }

        [TestCase]
        public void TryRemoveRepo_NonExistent_ReturnsFalse()
        {
            (LocalRepoRegistry registry, _) = this.CreateRegistry();
            registry.TryRemoveRepo(Repo1, out string error).ShouldBeFalse();
            string.IsNullOrEmpty(error).ShouldBeFalse();
        }

        [TestCase]
        public void TryGetActiveRepos_FiltersInactive()
        {
            (LocalRepoRegistry registry, _) = this.CreateRegistry();
            string ownerSID = Guid.NewGuid().ToString();

            registry.TryRegisterRepo(Repo1, ownerSID, out _).ShouldBeTrue();
            registry.TryRegisterRepo(Repo2, ownerSID, out _).ShouldBeTrue();
            registry.TryRegisterRepo(Repo3, ownerSID, out _).ShouldBeTrue();
            registry.TryDeactivateRepo(Repo2, out _).ShouldBeTrue();

            registry.TryGetActiveRepos(out List<LocalRepoRegistration> active, out _).ShouldBeTrue();
            active.Count.ShouldEqual(2);
            active.Any(r => r.EnlistmentRoot.Equals(Repo1)).ShouldBeTrue();
            active.Any(r => r.EnlistmentRoot.Equals(Repo3)).ShouldBeTrue();
            active.Any(r => r.EnlistmentRoot.Equals(Repo2)).ShouldBeFalse();
        }

        [TestCase]
        public void TryGetActiveRepos_EmptyRegistry_ReturnsEmptyList()
        {
            (LocalRepoRegistry registry, _) = this.CreateRegistry();

            registry.TryGetActiveRepos(out List<LocalRepoRegistration> active, out string error).ShouldBeTrue(error);
            active.Count.ShouldEqual(0);
        }

        [TestCase]
        public void ReadRegistry_NoRegistryFile_ReturnsEmpty()
        {
            (LocalRepoRegistry registry, _) = this.CreateRegistry();
            registry.ReadRegistry().Count.ShouldEqual(0);
        }

        [TestCase]
        public void ReadRegistry_HigherVersionOnDisk_ReturnsEmptyAndDoesNotOverwrite()
        {
            // Simulate a newer GVFS having written the registry.
            // We must read as empty AND must NOT overwrite when a subsequent
            // write happens, so the newer GVFS's data is preserved.
            (LocalRepoRegistry registry, MockFileSystem fs) = this.CreateRegistry();
            string registryPath = Path.Combine(DataLocation, LocalRepoRegistry.RegistryFileName);
            string futureContent = "99\n{\"EnlistmentRoot\":\"" + Repo1.Replace("\\", "\\\\") + "\",\"OwnerSID\":\"future\",\"IsActive\":true}\n";
            fs.WriteAllText(registryPath, futureContent);

            registry.ReadRegistry().Count.ShouldEqual(0);
        }

        [TestCase]
        public void ReadRegistry_MalformedLine_SkippedNotFatal()
        {
            (LocalRepoRegistry registry, MockFileSystem fs) = this.CreateRegistry();
            string registryPath = Path.Combine(DataLocation, LocalRepoRegistry.RegistryFileName);
            string contents =
                "2\n" +
                "{ this is not valid json }\n" +
                "{\"EnlistmentRoot\":\"" + Repo1.Replace("\\", "\\\\") + "\",\"OwnerSID\":\"sid\",\"IsActive\":true}\n";
            fs.WriteAllText(registryPath, contents);

            Dictionary<string, LocalRepoRegistration> all = registry.ReadRegistry();
            all.Count.ShouldEqual(1);
            all[Repo1].OwnerSID.ShouldEqual("sid");
        }

        [TestCase]
        public void ReadRegistry_BlankLinesIgnored()
        {
            (LocalRepoRegistry registry, MockFileSystem fs) = this.CreateRegistry();
            string registryPath = Path.Combine(DataLocation, LocalRepoRegistry.RegistryFileName);
            string contents =
                "2\n" +
                "\n" +
                "{\"EnlistmentRoot\":\"" + Repo1.Replace("\\", "\\\\") + "\",\"OwnerSID\":\"sid\",\"IsActive\":true}\n" +
                "\n";
            fs.WriteAllText(registryPath, contents);

            registry.ReadRegistry().Count.ShouldEqual(1);
        }

        [TestCase]
        public void ReadRegistry_OnDiskFormatMatchesServiceRegistry()
        {
            // The on-disk format MUST be wire-compatible with
            // GVFS.Service.RepoRegistry: first line is the version
            // (a bare integer); subsequent lines are JSON objects with
            // EnlistmentRoot / OwnerSID / IsActive fields.
            (LocalRepoRegistry registry, MockFileSystem fs) = this.CreateRegistry();
            string sid = Guid.NewGuid().ToString();
            registry.TryRegisterRepo(Repo1, sid, out _).ShouldBeTrue();

            string raw = fs.ReadAllText(Path.Combine(DataLocation, LocalRepoRegistry.RegistryFileName));
            string[] lines = raw.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

            // Version line
            lines[0].ShouldEqual(LocalRepoRegistry.RegistryVersion.ToString());

            // Entry line is JSON with the three required fields
            lines.Length.ShouldEqual(2);
            lines[1].Contains("\"EnlistmentRoot\"").ShouldBeTrue();
            lines[1].Contains("\"OwnerSID\"").ShouldBeTrue();
            lines[1].Contains("\"IsActive\"").ShouldBeTrue();
            lines[1].Contains(sid).ShouldBeTrue();
        }

        [TestCase]
        public void RegisterAfterRead_PreservesOtherEntriesWrittenByAnotherProcess()
        {
            // Simulate another process having written an entry between
            // construction and our register call: we read fresh on each
            // operation, so the other entry must survive.
            (LocalRepoRegistry registry, MockFileSystem fs) = this.CreateRegistry();
            string sid = Guid.NewGuid().ToString();

            string contents =
                "2\n" +
                "{\"EnlistmentRoot\":\"" + Repo2.Replace("\\", "\\\\") + "\",\"OwnerSID\":\"" + sid + "\",\"IsActive\":true}\n";
            fs.WriteAllText(Path.Combine(DataLocation, LocalRepoRegistry.RegistryFileName), contents);

            registry.TryRegisterRepo(Repo1, sid, out _).ShouldBeTrue();

            Dictionary<string, LocalRepoRegistration> all = registry.ReadRegistry();
            all.Count.ShouldEqual(2);
            all.ContainsKey(Repo1).ShouldBeTrue();
            all.ContainsKey(Repo2).ShouldBeTrue();
        }

        [TestCase]
        public void Constructor_NullArgs_Throws()
        {
            MockFileSystem fs = new MockFileSystem(new MockDirectory(DataLocation, null, null));
            Assert.Throws<ArgumentNullException>(() => new LocalRepoRegistry(null, fs, DataLocation));
            Assert.Throws<ArgumentNullException>(() => new LocalRepoRegistry(new MockTracer(), null, DataLocation));
            Assert.Throws<ArgumentNullException>(() => new LocalRepoRegistry(new MockTracer(), fs, null));
        }

        [TestCase]
        public void LocalRepoRegistration_JsonRoundTrip()
        {
            LocalRepoRegistration original = new LocalRepoRegistration("path", "sid") { IsActive = false };
            string json = original.ToJson();
            LocalRepoRegistration roundTripped = LocalRepoRegistration.FromJson(json);

            roundTripped.EnlistmentRoot.ShouldEqual(original.EnlistmentRoot);
            roundTripped.OwnerSID.ShouldEqual(original.OwnerSID);
            roundTripped.IsActive.ShouldEqual(original.IsActive);
        }

        private (LocalRepoRegistry registry, MockFileSystem fs) CreateRegistry()
        {
            MockFileSystem fs = new MockFileSystem(new MockDirectory(DataLocation, null, null));
            LocalRepoRegistry registry = new LocalRepoRegistry(new MockTracer(), fs, DataLocation);
            return (registry, fs);
        }

        private static void VerifyEntry(LocalRepoRegistration entry, string expectedOwnerSID, bool expectedIsActive)
        {
            entry.ShouldNotBeNull();
            entry.OwnerSID.ShouldEqual(expectedOwnerSID);
            entry.IsActive.ShouldEqual(expectedIsActive);
        }
    }
}
