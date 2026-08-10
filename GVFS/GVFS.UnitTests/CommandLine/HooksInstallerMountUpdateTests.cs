using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.FileSystem;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;

namespace GVFS.UnitTests.CommandLine
{
    [TestFixture]
    public class HooksInstallerMountUpdateTests
    {
        private const string HookName = "GVFS.ReadObjectHook";
        private const string RootPath = "mock:";
        private static readonly string InstalledDir = Path.Combine(RootPath, "installed");
        private static readonly string EnlistmentDir = Path.Combine(RootPath, "enlistment");
        private static readonly string InstalledHookPath = Path.Combine(InstalledDir, "GVFS.ReadObjectHook.exe");
        private static readonly string EnlistmentHookPath = Path.Combine(EnlistmentDir, "GVFS.ReadObjectHook.exe");

        private const string InstalledContent = "installed-hook-binary-v2";
        private const string OldEnlistmentContent = "old-hook-binary-v1";

        [TestCase]
        public void IdenticalHookIsNotCopied()
        {
            CopyControllableFileSystem fileSystem = this.CreateFileSystem(enlistmentContent: InstalledContent);
            GVFSContext context = this.CreateContext(fileSystem);

            bool result = HooksInstaller.TryUpdateHook(context, HookName, InstalledHookPath, EnlistmentHookPath, out string errorMessage);

            result.ShouldBeTrue(errorMessage);
            errorMessage.ShouldBeNull();
            fileSystem.CopyAttempts.ShouldEqual(0, "Identical hooks must not be re-copied");
        }

        [TestCase]
        public void DifferentHookIsCopiedOnce()
        {
            CopyControllableFileSystem fileSystem = this.CreateFileSystem(enlistmentContent: OldEnlistmentContent);
            GVFSContext context = this.CreateContext(fileSystem);

            bool result = HooksInstaller.TryUpdateHook(context, HookName, InstalledHookPath, EnlistmentHookPath, out string errorMessage);

            result.ShouldBeTrue(errorMessage);
            errorMessage.ShouldBeNull();
            fileSystem.CopyAttempts.ShouldEqual(1);
            fileSystem.ReadAllText(EnlistmentHookPath).ShouldEqual(InstalledContent);
        }

        [TestCase]
        public void MissingHookIsCopied()
        {
            CopyControllableFileSystem fileSystem = this.CreateFileSystem(enlistmentContent: null);
            GVFSContext context = this.CreateContext(fileSystem);

            bool result = HooksInstaller.TryUpdateHook(context, HookName, InstalledHookPath, EnlistmentHookPath, out string errorMessage);

            result.ShouldBeTrue(errorMessage);
            errorMessage.ShouldBeNull();
            fileSystem.CopyAttempts.ShouldEqual(1);
            fileSystem.ReadAllText(EnlistmentHookPath).ShouldEqual(InstalledContent);
        }

        [TestCase]
        public void HookIsCopiedWhenVersionDiffersEvenIfContentIsIdentical()
        {
            // Production compares FileVersion, not content. If the version differs, the hook
            // must be refreshed even when the bytes happen to match. The double models version
            // independently of content so this case is representable.
            CopyControllableFileSystem fileSystem = this.CreateFileSystem(enlistmentContent: InstalledContent);
            fileSystem.SetFileVersion(InstalledHookPath, "2.0.0.0");
            fileSystem.SetFileVersion(EnlistmentHookPath, "1.0.0.0");
            GVFSContext context = this.CreateContext(fileSystem);

            bool result = HooksInstaller.TryUpdateHook(context, HookName, InstalledHookPath, EnlistmentHookPath, out string errorMessage);

            result.ShouldBeTrue(errorMessage);
            errorMessage.ShouldBeNull();
            fileSystem.CopyAttempts.ShouldEqual(1, "A version difference must trigger a copy even when content matches");
            fileSystem.GetFileVersion(EnlistmentHookPath).ShouldEqual("2.0.0.0", "The copied hook must carry the installed version");
        }

        [TestCase]
        public void HookIsNotCopiedWhenVersionMatchesEvenIfContentDiffers()
        {
            // Production compares FileVersion, not content. If the version matches, no copy
            // happens even when the bytes differ. This pins that the comparison is by version.
            CopyControllableFileSystem fileSystem = this.CreateFileSystem(enlistmentContent: OldEnlistmentContent);
            fileSystem.SetFileVersion(InstalledHookPath, "2.0.0.0");
            fileSystem.SetFileVersion(EnlistmentHookPath, "2.0.0.0");
            GVFSContext context = this.CreateContext(fileSystem);

            bool result = HooksInstaller.TryUpdateHook(context, HookName, InstalledHookPath, EnlistmentHookPath, out string errorMessage);

            result.ShouldBeTrue(errorMessage);
            errorMessage.ShouldBeNull();
            fileSystem.CopyAttempts.ShouldEqual(0, "A matching version must not trigger a copy even when content differs");
        }

        [TestCase]
        public void HookIsCopiedWhenBothVersionsAreNull()
        {
            // GetFileVersion returns null for a binary with no version resource. Two null
            // versions must NOT be treated as identical (string.Equals(null, null) == true);
            // otherwise a version-less hook would never be refreshed. The hook must be copied.
            CopyControllableFileSystem fileSystem = this.CreateFileSystem(enlistmentContent: InstalledContent);
            fileSystem.SetFileVersion(InstalledHookPath, null);
            fileSystem.SetFileVersion(EnlistmentHookPath, null);
            GVFSContext context = this.CreateContext(fileSystem);

            bool result = HooksInstaller.TryUpdateHook(context, HookName, InstalledHookPath, EnlistmentHookPath, out string errorMessage);

            result.ShouldBeTrue(errorMessage);
            errorMessage.ShouldBeNull();
            fileSystem.CopyAttempts.ShouldEqual(1, "An unknown (null) version must force a copy rather than be assumed identical");
        }

        [TestCase]
        public void TransientCopyFailureIsRetriedAndSucceeds()
        {
            CopyControllableFileSystem fileSystem = this.CreateFileSystem(enlistmentContent: OldEnlistmentContent);
            fileSystem.FailCopyCount = 2;
            GVFSContext context = this.CreateContext(fileSystem);

            bool result = HooksInstaller.TryUpdateHook(context, HookName, InstalledHookPath, EnlistmentHookPath, out string errorMessage);

            result.ShouldBeTrue(errorMessage);
            errorMessage.ShouldBeNull();
            fileSystem.CopyAttempts.ShouldEqual(3, "The copy must be retried past two transient failures");
            fileSystem.ReadAllText(EnlistmentHookPath).ShouldEqual(InstalledContent);
        }

        [TestCase]
        public void LockedButAlreadyCorrectHookDoesNotFailMount()
        {
            CopyControllableFileSystem fileSystem = this.CreateFileSystem(enlistmentContent: OldEnlistmentContent);
            fileSystem.AlwaysFailCopy = true;
            fileSystem.WriteCorrectDestinationOnFailure = true;
            MockTracer tracer = new MockTracer();
            GVFSContext context = this.CreateContext(fileSystem, tracer);

            bool result = HooksInstaller.TryUpdateHook(context, HookName, InstalledHookPath, EnlistmentHookPath, out string errorMessage);

            result.ShouldBeTrue("A locked hook that already matches the installed hook must not fail the mount");
            errorMessage.ShouldBeNull();
            tracer.RelatedErrorEvents.Count.ShouldEqual(0, "A locked-but-correct hook must not log an error");
        }

        [TestCase]
        public void CompareFailureDoesNotHardFailMountAndRefreshesHook()
        {
            // The enlistment hook is transiently locked such that reading its version to
            // compare throws. The mount must not hard-fail on the compare; it must fall
            // through to the resilient copy path and refresh the hook.
            CopyControllableFileSystem fileSystem = this.CreateFileSystem(enlistmentContent: OldEnlistmentContent);
            fileSystem.ThrowOnGetVersionPath = EnlistmentHookPath;
            MockTracer tracer = new MockTracer();
            GVFSContext context = this.CreateContext(fileSystem, tracer);

            bool result = HooksInstaller.TryUpdateHook(context, HookName, InstalledHookPath, EnlistmentHookPath, out string errorMessage);

            result.ShouldBeTrue(errorMessage);
            errorMessage.ShouldBeNull();
            tracer.RelatedErrorEvents.Count.ShouldEqual(0, "A compare failure must not be fatal to the mount");
            fileSystem.CopyAttempts.ShouldBeAtLeast(1, "The hook must be refreshed after a compare failure");
            fileSystem.ReadAllText(EnlistmentHookPath).ShouldEqual(InstalledContent);
        }

        [TestCase]
        public void MissingInstalledHookFailsWithoutCopying()
        {
            CopyControllableFileSystem fileSystem = this.CreateFileSystem(enlistmentContent: OldEnlistmentContent, includeInstalled: false);
            GVFSContext context = this.CreateContext(fileSystem);

            bool result = HooksInstaller.TryUpdateHook(context, HookName, InstalledHookPath, EnlistmentHookPath, out string errorMessage);

            result.ShouldBeFalse();
            errorMessage.ShouldNotBeNull();
            errorMessage.ShouldContain("cannot be found");
            fileSystem.CopyAttempts.ShouldEqual(0, "A missing installed hook must not trigger a copy");
        }

        [TestCase]
        public void PersistentCopyFailureFailsMount()
        {
            CopyControllableFileSystem fileSystem = this.CreateFileSystem(enlistmentContent: OldEnlistmentContent);
            fileSystem.AlwaysFailCopy = true;
            MockTracer tracer = new MockTracer();
            GVFSContext context = this.CreateContext(fileSystem, tracer);

            bool result = HooksInstaller.TryUpdateHook(context, HookName, InstalledHookPath, EnlistmentHookPath, out string errorMessage);

            result.ShouldBeFalse();
            errorMessage.ShouldNotBeNull();
            errorMessage.ShouldContain(HookName);
            tracer.RelatedErrorEvents.Count.ShouldBeAtLeast(1);
        }

        private CopyControllableFileSystem CreateFileSystem(string enlistmentContent, bool includeInstalled = true)
        {
            MockDirectory root = new MockDirectory(
                RootPath,
                new[]
                {
                    new MockDirectory(InstalledDir, folders: null, files: null),
                    new MockDirectory(EnlistmentDir, folders: null, files: null),
                },
                files: null);

            CopyControllableFileSystem fileSystem = new CopyControllableFileSystem(root);
            if (includeInstalled)
            {
                fileSystem.WriteAllText(InstalledHookPath, InstalledContent);
            }

            if (enlistmentContent != null)
            {
                fileSystem.WriteAllText(EnlistmentHookPath, enlistmentContent);
            }

            return fileSystem;
        }

        private GVFSContext CreateContext(CopyControllableFileSystem fileSystem, MockTracer tracer = null)
        {
            return new GVFSContext(
                tracer ?? new MockTracer(),
                fileSystem,
                repository: null,
                new MockGVFSEnlistment());
        }

        private sealed class CopyControllableFileSystem : MockFileSystem
        {
            private readonly Dictionary<string, string> explicitVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public CopyControllableFileSystem(MockDirectory rootDirectory)
                : base(rootDirectory)
            {
            }

            public int CopyAttempts { get; private set; }

            public int FailCopyCount { get; set; }

            public bool AlwaysFailCopy { get; set; }

            public bool WriteCorrectDestinationOnFailure { get; set; }

            public string ThrowOnGetVersionPath { get; set; }

            /// <summary>
            /// Pins a path's FileVersion independently of its content, so tests can model the
            /// real decoupling between a PE version resource and file bytes (including a null
            /// version). A path with no explicit version falls back to its stored text, which
            /// keeps the common "version tracks content" tests simple.
            /// </summary>
            public void SetFileVersion(string path, string version)
            {
                this.explicitVersions[path] = version;
            }

            public override string GetFileVersion(string path)
            {
                if (this.ThrowOnGetVersionPath != null && path == this.ThrowOnGetVersionPath)
                {
                    throw new IOException("The process cannot access the file because it is being used by another process.");
                }

                if (this.explicitVersions.TryGetValue(path, out string version))
                {
                    return version;
                }

                return this.ReadAllText(path);
            }

            public override bool TryCopyToTempFileAndRename(string sourcePath, string destinationPath, out Exception handledException)
            {
                this.CopyAttempts++;

                if (this.AlwaysFailCopy || this.CopyAttempts <= this.FailCopyCount)
                {
                    if (this.WriteCorrectDestinationOnFailure)
                    {
                        // Simulate another writer (or the lock holder) leaving the correct
                        // binary in place even though our rename could not complete.
                        this.PropagateHook(sourcePath, destinationPath);
                    }

                    handledException = new Win32Exception(5, "Access is denied");
                    return false;
                }

                this.PropagateHook(sourcePath, destinationPath);
                handledException = null;
                return true;
            }

            // Model an on-disk copy: the destination takes the source's content AND its
            // version, so the two converge exactly as they would after a real file copy.
            private void PropagateHook(string sourcePath, string destinationPath)
            {
                this.WriteAllText(destinationPath, this.ReadAllText(sourcePath));

                if (this.explicitVersions.TryGetValue(sourcePath, out string sourceVersion))
                {
                    this.explicitVersions[destinationPath] = sourceVersion;
                }
                else
                {
                    this.explicitVersions.Remove(destinationPath);
                }
            }
        }
    }
}
