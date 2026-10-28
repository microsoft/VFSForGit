using GVFS.CommandLine;
using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using GVFS.RepairJobs;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System;
using System.IO;
using System.Text.Json;

namespace GVFS.UnitTests.CommandLine
{
    [TestFixture]
    public class GitIndexPathCallerTests
    {
        private string testRoot;

        [SetUp]
        public void SetUp()
        {
            if (GVFSPlatform.Instance == null)
            {
                GVFSPlatform.Register(new MockPlatform());
            }

            this.testRoot = Path.Combine(Path.GetTempPath(), "GitIndexPathCallerTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(this.testRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(this.testRoot))
            {
                Directory.Delete(this.testRoot, recursive: true);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GitIndexGeneratorUsesEnlistmentIndexPath(bool isWorktree)
        {
            GVFSEnlistment enlistment = this.CreateEnlistment(isWorktree);
            if (!isWorktree)
            {
                enlistment.GitIndexPath.ShouldEqual(Path.Combine(enlistment.DotGitRoot, "index"));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(enlistment.GitIndexPath));

            File.WriteAllText(enlistment.GitIndexPath, "old index");
            if (isWorktree)
            {
                File.WriteAllText(Path.Combine(enlistment.DotGitRoot, GVFSConstants.DotGit.IndexName), "shared index");
            }

            GitIndexGenerator generator = new GitIndexGenerator(new MockTracer(), enlistment, shouldHashIndex: false);
            generator.TemporaryIndexFilePath.ShouldEqual(enlistment.GitIndexPath + ".lock2");
            File.WriteAllText(generator.TemporaryIndexFilePath, "new index");

            generator.ReplaceExistingIndex();

            File.ReadAllText(enlistment.GitIndexPath).ShouldEqual("new index");
            if (isWorktree)
            {
                File.ReadAllText(Path.Combine(enlistment.DotGitRoot, GVFSConstants.DotGit.IndexName)).ShouldEqual("shared index");
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GitIndexRepairJobUsesEnlistmentIndexPath(bool isWorktree)
        {
            GVFSEnlistment enlistment = this.CreateEnlistment(isWorktree);
            GitIndexRepairJob repairJob = new GitIndexRepairJob(new MockTracer(), TextWriter.Null, enlistment);

            repairJob.IndexPath.ShouldEqual(enlistment.GitIndexPath);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        public void DehydrateBacksUpEnlistmentIndexPath(bool isWorktree, bool move)
        {
            GVFSEnlistment enlistment = this.CreateEnlistment(isWorktree);
            string indexDirectory = Path.GetDirectoryName(enlistment.GitIndexPath);
            Directory.CreateDirectory(indexDirectory);
            File.WriteAllText(enlistment.GitIndexPath, "working tree index");

            string sharedIndexPath = Path.Combine(enlistment.DotGitRoot, GVFSConstants.DotGit.IndexName);
            if (isWorktree)
            {
                File.WriteAllText(sharedIndexPath, "shared index");
            }

            string backupGit = Path.Combine(this.testRoot, "backup-" + isWorktree + "-" + move, GVFSConstants.DotGit.Root);
            Directory.CreateDirectory(backupGit);

            DehydrateVerb verb = new DehydrateVerb();
            bool result = verb.TryBackupGitIndex(
                new MockTracer(),
                enlistment,
                backupGit,
                move,
                out string errorMessage);

            result.ShouldEqual(true, errorMessage);
            File.ReadAllText(Path.Combine(backupGit, GVFSConstants.DotGit.IndexName)).ShouldEqual("working tree index");
            File.Exists(enlistment.GitIndexPath).ShouldEqual(!move);
            if (isWorktree)
            {
                File.ReadAllText(sharedIndexPath).ShouldEqual("shared index");
            }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        public void DehydrateBacksUpOnlyCurrentWorkingTreeLocks(bool isWorktree, bool move)
        {
            GVFSEnlistment enlistment = this.CreateEnlistment(isWorktree);
            string indexDirectory = Path.GetDirectoryName(enlistment.GitIndexPath);
            Directory.CreateDirectory(indexDirectory);
            string indexLockPath = Path.Combine(indexDirectory, GVFSConstants.DotGit.IndexName + ".lock");
            File.WriteAllText(indexLockPath, "working tree lock");

            string sharedLockPath = Path.Combine(enlistment.DotGitRoot, "shared.lock");
            if (isWorktree)
            {
                File.WriteAllText(sharedLockPath, "shared lock");
            }

            string backupGit = Path.Combine(this.testRoot, "lock-backup-" + isWorktree + "-" + move, GVFSConstants.DotGit.Root);
            Directory.CreateDirectory(backupGit);

            DehydrateVerb verb = new DehydrateVerb();
            bool result = verb.TryBackupGitLocks(
                new MockTracer(),
                enlistment,
                backupGit,
                move,
                out string errorMessage);

            result.ShouldEqual(true, errorMessage);
            File.ReadAllText(Path.Combine(backupGit, GVFSConstants.DotGit.IndexName + ".lock")).ShouldEqual("working tree lock");
            File.Exists(indexLockPath).ShouldEqual(!move);
            if (isWorktree)
            {
                File.ReadAllText(sharedLockPath).ShouldEqual("shared lock");
                File.Exists(Path.Combine(backupGit, "shared.lock")).ShouldBeFalse();
            }
        }

        [TestCase]
        public void FullDehydrateRejectsWorktree()
        {
            GVFSEnlistment enlistment = this.CreateEnlistment(isWorktree: true);
            StringWriter output = new StringWriter();
            DehydrateVerb verb = new DehydrateVerb
            {
                Full = true,
                Output = output,
            };

            Assert.Throws<GVFSVerb.VerbAbortedException>(
                () => verb.ValidateFullDehydrate(new MockTracer(), enlistment));

            output.ToString().ShouldContain("Dehydrate --full is not supported for git worktrees.");
        }

        [TestCase]
        public void FullDehydrateRejectsPrimaryWithLinkedWorktree()
        {
            GVFSEnlistment enlistment = this.CreateEnlistment(isWorktree: false);
            string worktreeGitDir = Path.Combine(enlistment.DotGitRoot, "worktrees", "linked-worktree");
            Directory.CreateDirectory(worktreeGitDir);
            File.WriteAllText(
                Path.Combine(worktreeGitDir, "gitdir"),
                Path.Combine(this.testRoot, "linked-worktree", GVFSConstants.DotGit.Root));
            Directory.CreateDirectory(Path.Combine(enlistment.DotGitRoot, "worktrees", "not-a-registration"));

            StringWriter output = new StringWriter();
            DehydrateVerb verb = new DehydrateVerb
            {
                Full = true,
                Output = output,
            };

            Assert.Throws<GVFSVerb.VerbAbortedException>(
                () => verb.ValidateFullDehydrate(new MockTracer(), enlistment));

            output.ToString().ShouldContain("Dehydrate --full is not supported while linked worktrees exist.");
            output.ToString().ShouldContain(Path.Combine(this.testRoot, "linked-worktree"));
            output.ToString().ShouldContain("git worktree remove <path>");
            output.ToString().ShouldContain("git worktree prune");
            output.ToString().ShouldContain("git worktree unlock <path>");
            output.ToString().ShouldContain("back up or push");
            output.ToString().Contains("not-a-registration").ShouldBeFalse();
        }

        [TestCase]
        public void FullDehydrateResolvesRelativeRegistration()
        {
            GVFSEnlistment enlistment = this.CreateEnlistment(isWorktree: false);
            string worktreeGitDir = Path.Combine(enlistment.DotGitRoot, "worktrees", "relative");
            Directory.CreateDirectory(worktreeGitDir);
            string worktreePath = Path.Combine(this.testRoot, "relative-worktree");
            File.WriteAllText(
                Path.Combine(worktreeGitDir, "gitdir"),
                Path.GetRelativePath(worktreeGitDir, Path.Combine(worktreePath, ".git")).Replace('\\', '/') + "\n");
            using (StringWriter output = new StringWriter())
            using (MockTracer tracer = new MockTracer())
            {
                DehydrateVerb verb = new DehydrateVerb { Full = true, Output = output };
                Assert.Throws<GVFSVerb.VerbAbortedException>(
                    () => verb.ValidateFullDehydrate(tracer, enlistment));
                output.ToString().ShouldContain("not supported while linked worktrees exist.");
                output.ToString().ShouldContain(worktreePath);
                output.ToString().Contains("Invalid worktree registration").ShouldBeFalse();
            }
        }

        [TestCase("\n")]
        [TestCase("   ")]
        [TestCase(" \n")]
        [TestCase("not-an-absolute-path")]
        [TestCase(@"C:\worktrees\bad\HEAD")]
        public void FullDehydrateRejectsMalformedRegistration(string registration)
        {
            GVFSEnlistment enlistment = this.CreateEnlistment(isWorktree: false);
            string worktreeGitDir = Path.Combine(enlistment.DotGitRoot, "worktrees", "bad");
            Directory.CreateDirectory(worktreeGitDir);
            File.WriteAllText(Path.Combine(worktreeGitDir, "gitdir"), registration);

            using (StringWriter output = new StringWriter())
            using (MockTracer tracer = new MockTracer())
            {
                DehydrateVerb verb = new DehydrateVerb { Full = true, Output = output };
                Assert.Throws<GVFSVerb.VerbAbortedException>(
                    () => verb.ValidateFullDehydrate(tracer, enlistment));
                output.ToString().ShouldContain("Full dehydrate is blocked.");
                output.ToString().ShouldContain(Path.Combine(worktreeGitDir, "gitdir"));
                output.ToString().ShouldContain("git worktree list");
                output.ToString().ShouldContain("git worktree prune");
                tracer.RelatedErrorEvents.Count.ShouldEqual(1);
                Directory.Exists(enlistment.WorkingDirectoryBackingRoot).ShouldBeTrue();
            }
        }

        [TestCase]
        public void FullDehydrateRejectsUnreadableRegistration()
        {
            GVFSEnlistment enlistment = this.CreateEnlistment(isWorktree: false);
            string worktreeGitDir = Path.Combine(enlistment.DotGitRoot, "worktrees", "locked");
            Directory.CreateDirectory(worktreeGitDir);
            string gitdirFile = Path.Combine(worktreeGitDir, "gitdir");
            File.WriteAllText(gitdirFile, Path.Combine(this.testRoot, "linked-worktree", GVFSConstants.DotGit.Root));

            using (FileStream locked = new FileStream(gitdirFile, FileMode.Open, FileAccess.Read, FileShare.None))
            using (StringWriter output = new StringWriter())
            using (MockTracer tracer = new MockTracer())
            {
                DehydrateVerb verb = new DehydrateVerb { Full = true, Output = output };
                Assert.Throws<GVFSVerb.VerbAbortedException>(() => verb.ValidateFullDehydrate(tracer, enlistment));
                output.ToString().ShouldContain("Full dehydrate is blocked.");
                output.ToString().ShouldContain(gitdirFile);
                tracer.RelatedErrorEvents.Count.ShouldEqual(1);
                tracer.RelatedWarningEvents.Count.ShouldEqual(0);
                using (JsonDocument metadata = JsonDocument.Parse(tracer.RelatedErrorEvents[0]))
                {
                    metadata.RootElement.GetProperty("Exception").GetString().ShouldContain("System.IO.IOException");
                    metadata.RootElement.GetProperty("Exception").GetString().ShouldContain("TryGetKnownWorktreePaths");
                }

                output.ToString().Contains("System.IO.IOException").ShouldBeFalse();
            }
        }

        [TestCase]
        public void FullDehydrateRejectsUnreadableRegistrationsDirectory()
        {
            GVFSEnlistment enlistment = this.CreateEnlistment(isWorktree: false);
            File.WriteAllText(Path.Combine(enlistment.DotGitRoot, "worktrees"), "not a directory");
            using (StringWriter output = new StringWriter())
            using (MockTracer tracer = new MockTracer())
            {
                DehydrateVerb verb = new DehydrateVerb { Full = true, Output = output };
                Assert.Throws<GVFSVerb.VerbAbortedException>(
                    () => verb.ValidateFullDehydrate(tracer, enlistment));
                output.ToString().ShouldContain("Full dehydrate is blocked.");
            }
        }

        [TestCase]
        public void FullDehydrateAllowsPrimaryWithoutLinkedWorktrees()
        {
            GVFSEnlistment enlistment = this.CreateEnlistment(isWorktree: false);
            DehydrateVerb verb = new DehydrateVerb
            {
                Full = true,
                Output = TextWriter.Null,
            };

            Assert.DoesNotThrow(
                () => verb.ValidateFullDehydrate(new MockTracer(), enlistment));
        }

        [TestCase]
        public void FullDehydrateAllowsEmptyRegistrationsDirectory()
        {
            GVFSEnlistment enlistment = this.CreateEnlistment(isWorktree: false);
            Directory.CreateDirectory(Path.Combine(enlistment.DotGitRoot, "worktrees"));
            using (MockTracer tracer = new MockTracer())
            {
                DehydrateVerb verb = new DehydrateVerb { Full = true, Output = TextWriter.Null };
                Assert.DoesNotThrow(() => verb.ValidateFullDehydrate(tracer, enlistment));
                tracer.RelatedErrorEvents.Count.ShouldEqual(0);
            }
        }

        [TestCase]
        public void FullDehydrateIgnoresDirectoryWithoutGitdir()
        {
            GVFSEnlistment enlistment = this.CreateEnlistment(isWorktree: false);
            Directory.CreateDirectory(Path.Combine(enlistment.DotGitRoot, "worktrees", "not-a-registration"));
            using (MockTracer tracer = new MockTracer())
            {
                DehydrateVerb verb = new DehydrateVerb { Full = true, Output = TextWriter.Null };
                Assert.DoesNotThrow(() => verb.ValidateFullDehydrate(tracer, enlistment));
                tracer.RelatedErrorEvents.Count.ShouldEqual(0);
            }
        }

        [TestCase]
        public void FullDehydrateIgnoresEmptyGitdir()
        {
            GVFSEnlistment enlistment = this.CreateEnlistment(isWorktree: false);
            string worktreeGitDir = Path.Combine(enlistment.DotGitRoot, "worktrees", "empty");
            Directory.CreateDirectory(worktreeGitDir);
            File.WriteAllBytes(Path.Combine(worktreeGitDir, "gitdir"), Array.Empty<byte>());
            using (MockTracer tracer = new MockTracer())
            {
                DehydrateVerb verb = new DehydrateVerb { Full = true, Output = TextWriter.Null };
                Assert.DoesNotThrow(() => verb.ValidateFullDehydrate(tracer, enlistment));
                tracer.RelatedErrorEvents.Count.ShouldEqual(0);
                tracer.RelatedWarningEvents.Count.ShouldEqual(0);
            }
        }

        [TestCase]
        public void FullDehydrateRejectsUnverifiableStateWhenAnotherWorktreeIsReadable()
        {
            GVFSEnlistment enlistment = this.CreateEnlistment(isWorktree: false);
            string validDir = Path.Combine(enlistment.DotGitRoot, "worktrees", "valid");
            string unreadableDir = Path.Combine(enlistment.DotGitRoot, "worktrees", "unreadable");
            Directory.CreateDirectory(validDir);
            Directory.CreateDirectory(unreadableDir);
            string worktreePath = Path.Combine(this.testRoot, "visible-worktree");
            File.WriteAllText(Path.Combine(validDir, "gitdir"), Path.Combine(worktreePath, ".git") + "\n");
            string unreadableFile = Path.Combine(unreadableDir, "gitdir");
            File.WriteAllText(unreadableFile, Path.Combine(this.testRoot, "hidden-worktree", ".git"));
            using (FileStream locked = new FileStream(unreadableFile, FileMode.Open, FileAccess.Read, FileShare.None))
            using (StringWriter output = new StringWriter())
            using (MockTracer tracer = new MockTracer())
            {
                DehydrateVerb verb = new DehydrateVerb { Full = true, Output = output };
                Assert.Throws<GVFSVerb.VerbAbortedException>(() => verb.ValidateFullDehydrate(tracer, enlistment));
                output.ToString().ShouldContain("Full dehydrate is blocked.");
                output.ToString().ShouldContain(unreadableFile);
                tracer.RelatedWarningEvents.Count.ShouldEqual(0);
                tracer.RelatedErrorEvents.Count.ShouldEqual(1);
            }
        }

        [TestCase(false, "HEAD")]
        [TestCase(true, "HEAD")]
        [TestCase(false, "commondir")]
        [TestCase(true, "commondir")]
        [TestCase(false, ".gvfs")]
        [TestCase(true, ".gvfs")]
        public void FullDehydrateRejectsMissingOrEmptyPointerWithRemainingMetadata(bool emptyPointer, string metadataName)
        {
            GVFSEnlistment enlistment = this.CreateEnlistment(isWorktree: false);
            string entry = Path.Combine(enlistment.DotGitRoot, "worktrees", "incomplete");
            Directory.CreateDirectory(entry);
            if (emptyPointer)
            {
                File.WriteAllBytes(Path.Combine(entry, "gitdir"), Array.Empty<byte>());
            }

            string metadataPath = Path.Combine(entry, metadataName);
            if (metadataName == ".gvfs")
            {
                Directory.CreateDirectory(metadataPath);
            }
            else
            {
                File.WriteAllText(metadataPath, "remaining metadata");
            }

            using (StringWriter output = new StringWriter())
            using (MockTracer tracer = new MockTracer())
            {
                DehydrateVerb verb = new DehydrateVerb { Full = true, Output = output };
                Assert.Throws<GVFSVerb.VerbAbortedException>(() => verb.ValidateFullDehydrate(tracer, enlistment));
                output.ToString().ShouldContain("Full dehydrate is blocked.");
                output.ToString().ShouldContain(metadataPath);
                tracer.RelatedErrorEvents.Count.ShouldEqual(1);
            }
        }

        [TestCase]
        public void FullDehydrateRejectsMissingGitDirectory()
        {
            GVFSEnlistment enlistment = this.CreateEnlistment(isWorktree: false);
            Directory.Delete(enlistment.DotGitRoot);
            using (StringWriter output = new StringWriter())
            using (MockTracer tracer = new MockTracer())
            {
                DehydrateVerb verb = new DehydrateVerb { Full = true, Output = output };
                Assert.Throws<GVFSVerb.VerbAbortedException>(
                    () => verb.ValidateFullDehydrate(tracer, enlistment));
                output.ToString().ShouldContain("Full dehydrate is blocked.");
                tracer.RelatedErrorEvents.Count.ShouldEqual(1);
            }
        }

        [TestCase]
        public void FullDehydrateRejectsGitDirectoryReplacedByFile()
        {
            GVFSEnlistment enlistment = this.CreateEnlistment(isWorktree: false);
            Directory.Delete(enlistment.DotGitRoot);
            File.WriteAllText(enlistment.DotGitRoot, "not a directory");
            using (StringWriter output = new StringWriter())
            using (MockTracer tracer = new MockTracer())
            {
                DehydrateVerb verb = new DehydrateVerb { Full = true, Output = output };
                Assert.Throws<GVFSVerb.VerbAbortedException>(
                    () => verb.ValidateFullDehydrate(tracer, enlistment));
                output.ToString().ShouldContain("Full dehydrate is blocked.");
                tracer.RelatedErrorEvents.Count.ShouldEqual(1);
            }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void BackupRootIsUniqueAndWorktreeLocal(bool isWorktree, bool trailingSeparator)
        {
            GVFSEnlistment enlistment = this.CreateEnlistment(isWorktree, trailingSeparator: trailingSeparator);
            DateTime timestamp = new DateTime(2026, 9, 23, 13, 0, 0, 123);

            string backupRoot = DehydrateVerb.GetBackupRoot(enlistment, timestamp);

            string expectedParent = isWorktree
                ? this.testRoot
                : enlistment.PrimaryEnlistmentRoot;
            string expectedSubfolder = isWorktree
                ? Path.Combine("dehydrate_backup", enlistment.Worktree.Name)
                : "dehydrate_backup";
            backupRoot.ShouldEqual(
                Path.Combine(
                    expectedParent,
                    expectedSubfolder,
                    "20260923_130000_1230000"));
            DehydrateVerb.GetBackupRoot(enlistment, timestamp.AddTicks(1)).ShouldNotEqual(backupRoot);
        }

        [TestCase]
        public void MissingIndexReturnsBackupError()
        {
            GVFSEnlistment enlistment = this.CreateEnlistment(isWorktree: true);
            string backupGit = Path.Combine(this.testRoot, "missing-index-backup");
            Directory.CreateDirectory(backupGit);

            DehydrateVerb verb = new DehydrateVerb();
            bool result = verb.TryBackupGitIndex(
                new MockTracer(),
                enlistment,
                backupGit,
                move: false,
                out string errorMessage);

            result.ShouldBeFalse();
            errorMessage.ShouldContain(enlistment.GitIndexPath);
            errorMessage.ShouldContain(Path.Combine(backupGit, "index"));
        }

        [TestCase]
        public void MissingLockDirectoryReturnsBackupError()
        {
            GVFSEnlistment enlistment = this.CreateEnlistment(isWorktree: true);
            Directory.Delete(Path.GetDirectoryName(enlistment.GitIndexPath));
            string backupGit = Path.Combine(this.testRoot, "missing-lock-backup");
            Directory.CreateDirectory(backupGit);

            DehydrateVerb verb = new DehydrateVerb();
            bool result = verb.TryBackupGitLocks(
                new MockTracer(),
                enlistment,
                backupGit,
                move: false,
                out string errorMessage);

            result.ShouldBeFalse();
            errorMessage.ShouldNotBeNull();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LockBackupReturnsPreciseFileError(bool move)
        {
            GVFSEnlistment enlistment = this.CreateEnlistment(isWorktree: false);
            string lockPath = enlistment.GitIndexPath + ".lock";
            File.WriteAllText(lockPath, "lock content");
            string backupGit = Path.Combine(this.testRoot, "occupied-backup");
            Directory.CreateDirectory(backupGit);
            string backupLockPath = Path.Combine(backupGit, Path.GetFileName(lockPath));
            File.WriteAllText(backupLockPath, "previous backup");

            using (MockTracer tracer = new MockTracer())
            {
                DehydrateVerb verb = new DehydrateVerb();
                verb.TryBackupGitLocks(tracer, enlistment, backupGit, move, out string errorMessage).ShouldBeFalse();
                errorMessage.ShouldContain(lockPath);
                errorMessage.ShouldContain(backupLockPath);
                using (JsonDocument errorEvent = JsonDocument.Parse(tracer.RelatedErrorEvents[0]))
                {
                    errorMessage.ShouldContain(errorEvent.RootElement.GetProperty("Error").GetString());
                    errorEvent.RootElement.GetProperty("Exception").GetString().ShouldContain(nameof(IOException));
                }

                File.ReadAllText(lockPath).ShouldEqual("lock content");
                File.ReadAllText(backupLockPath).ShouldEqual("previous backup");
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void IndexBackupReturnsPreciseFileError(bool move)
        {
            GVFSEnlistment enlistment = this.CreateEnlistment(isWorktree: true);
            File.WriteAllText(enlistment.GitIndexPath, "worktree index");
            string backupGit = Path.Combine(this.testRoot, "occupied-index-backup");
            Directory.CreateDirectory(backupGit);
            string backupIndex = Path.Combine(backupGit, "index");
            File.WriteAllText(backupIndex, "previous backup");

            using (MockTracer tracer = new MockTracer())
            {
                DehydrateVerb verb = new DehydrateVerb();
                verb.TryBackupGitIndex(tracer, enlistment, backupGit, move, out string errorMessage).ShouldBeFalse();
                errorMessage.ShouldContain(enlistment.GitIndexPath);
                errorMessage.ShouldContain(backupIndex);
                using (JsonDocument metadata = JsonDocument.Parse(tracer.RelatedErrorEvents[0]))
                {
                    errorMessage.ShouldContain(metadata.RootElement.GetProperty("Error").GetString());
                }

                File.ReadAllText(enlistment.GitIndexPath).ShouldEqual("worktree index");
                File.ReadAllText(backupIndex).ShouldEqual("previous backup");
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void IndexGenerationTracesResolvedDestination(bool isWorktree)
        {
            GVFSEnlistment enlistment = this.CreateEnlistment(isWorktree, gitBinPath: "git.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(enlistment.GitIndexPath));
            using (JsonTracer tracer = new JsonTracer("Microsoft-GVFS-Test", "IndexGeneration", disableTelemetry: true))
            using (IndexGenerationListener listener = new IndexGenerationListener())
            {
                tracer.AddEventListener(listener);
                GitIndexGenerator generator = new GitIndexGenerator(tracer, enlistment, shouldHashIndex: false);
                // No repository exists here. Git returns a failure without contacting a remote.
                generator.CreateFromRef("HEAD", indexVersion: 4, isFinal: false);

                generator.HasFailures.ShouldBeTrue();
                listener.ActivityPayload.ShouldNotBeNull();
                using (JsonDocument metadata = JsonDocument.Parse(listener.ActivityPayload))
                {
                    metadata.RootElement.GetProperty("IndexPath").GetString().ShouldEqual(enlistment.GitIndexPath);
                    metadata.RootElement.GetProperty("IsWorktree").GetBoolean().ShouldEqual(isWorktree);
                }
            }
        }

        private GVFSEnlistment CreateEnlistment(bool isWorktree, string gitBinPath = null, bool trailingSeparator = false)
        {
            gitBinPath = gitBinPath ?? Path.Combine(this.testRoot, "git.exe");
            string primaryRoot = Path.Combine(this.testRoot, isWorktree ? "worktree-primary" : "standard");
            string primarySrc = Path.Combine(primaryRoot, GVFSConstants.WorkingDirectoryRootName);
            string sharedGitDir = Path.Combine(primarySrc, GVFSConstants.DotGit.Root);
            Directory.CreateDirectory(sharedGitDir);

            if (!isWorktree)
            {
                return new GVFSEnlistment(primaryRoot, "mock://repo", gitBinPath, authentication: null);
            }

            string worktreePath = Path.Combine(this.testRoot, "linked-worktree");
            if (trailingSeparator)
            {
                worktreePath += Path.DirectorySeparatorChar;
            }

            string worktreeGitDir = Path.Combine(sharedGitDir, "worktrees", "linked-worktree");
            Directory.CreateDirectory(worktreePath);
            Directory.CreateDirectory(worktreeGitDir);

            GVFSEnlistment.WorktreeInfo worktreeInfo = new GVFSEnlistment.WorktreeInfo
            {
                Name = "linked-worktree",
                WorktreePath = worktreePath,
                WorktreeGitDir = worktreeGitDir,
                SharedGitDir = sharedGitDir,
                PipeSuffix = "_WT_LINKED-WORKTREE",
            };

            return GVFSEnlistment.CreateForWorktree(
                primaryRoot,
                gitBinPath,
                authentication: null,
                worktreeInfo,
                repoUrl: "mock://repo");
        }

        private class IndexGenerationListener : EventListener
        {
            public IndexGenerationListener()
                : base(EventLevel.Informational, Keywords.Any, null)
            {
            }

            public string ActivityPayload { get; private set; }

            protected override void RecordMessageInternal(TraceEventMessage message)
            {
                if (message.EventName == "CreateFromHeadTree" && message.Opcode == EventOpcode.Start)
                {
                    this.ActivityPayload = message.Payload;
                }
            }
        }
    }
}
