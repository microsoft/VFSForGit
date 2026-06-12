using GVFS.Common;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class LogonTaskRegistrationTests
    {
        private const string TestGvfsPath = @"C:\Users\test\AppData\Local\Programs\GVFS\Current\gvfs.exe";
        private const string TestUserSid = "S-1-5-21-1111-2222-3333-1001";
        private const string OtherUserSid = "S-1-5-21-1111-2222-3333-1002";

        [TestCase]
        public void TemplateHash_IsStableAcrossCalls()
        {
            // Same template content => same hash, every time.
            LogonTaskRegistration.TemplateHash.ShouldEqual(LogonTaskRegistration.TemplateHash);
            // Full SHA-256 hex = 64 chars
            LogonTaskRegistration.TemplateHash.Length.ShouldEqual(64);
        }

        [TestCase]
        public void BuildTaskXml_SubstitutesAllPlaceholders()
        {
            string xml = LogonTaskRegistration.BuildTaskXml(TestGvfsPath, TestUserSid);

            xml.Contains(LogonTaskRegistration.GvfsPathPlaceholder).ShouldBeFalse("no GVFS_PATH placeholder should remain");
            xml.Contains(LogonTaskRegistration.UserIdPlaceholder).ShouldBeFalse("no USER_ID placeholder should remain");
            xml.Contains(LogonTaskRegistration.TaskHashPlaceholder).ShouldBeFalse("no TASK_HASH placeholder should remain");

            xml.Contains(TestGvfsPath).ShouldBeTrue("gvfs.exe path should appear in the XML");
            xml.Contains(TestUserSid).ShouldBeTrue("user SID should appear in the XML");
            xml.Contains(LogonTaskRegistration.TemplateHash).ShouldBeTrue("template hash should appear in the XML");
        }

        [TestCase]
        public void BuildTaskXml_ProducesMountAllArguments()
        {
            string xml = LogonTaskRegistration.BuildTaskXml(TestGvfsPath, TestUserSid);
            // The task action runs `gvfs.exe service --mount-all`.
            xml.Contains("service --mount-all").ShouldBeTrue();
        }

        [TestCase]
        public void BuildTaskXml_NullOrEmptyArgsThrow()
        {
            // Assert.Catch accepts derived types (ArgumentNullException is
            // also raised by ThrowIfNullOrEmpty for null inputs).
            Assert.Catch<ArgumentException>(() => LogonTaskRegistration.BuildTaskXml(null, TestUserSid));
            Assert.Catch<ArgumentException>(() => LogonTaskRegistration.BuildTaskXml("", TestUserSid));
            Assert.Catch<ArgumentException>(() => LogonTaskRegistration.BuildTaskXml(TestGvfsPath, null));
            Assert.Catch<ArgumentException>(() => LogonTaskRegistration.BuildTaskXml(TestGvfsPath, ""));
        }

        [TestCase]
        public void TryExtractHashMarker_FindsMarkerInDescription()
        {
            string description = "Mounts the user's enlistments at logon. [gvfs-logon-task-hash=DEADBEEF12345678ABCDEF0123456789FEDCBA9876543210CAFEBABE12345678]";
            LogonTaskRegistration.TryExtractHashMarker(description, out string hash).ShouldBeTrue();
            hash.ShouldEqual("DEADBEEF12345678ABCDEF0123456789FEDCBA9876543210CAFEBABE12345678");
        }

        [TestCase]
        public void TryExtractHashMarker_NoMarker_ReturnsFalse()
        {
            LogonTaskRegistration.TryExtractHashMarker("Just a plain description.", out string hash).ShouldBeFalse();
            hash.ShouldBeNull();
        }

        [TestCase]
        public void TryExtractHashMarker_EmptyOrNull_ReturnsFalse()
        {
            LogonTaskRegistration.TryExtractHashMarker(null, out _).ShouldBeFalse();
            LogonTaskRegistration.TryExtractHashMarker("", out _).ShouldBeFalse();
        }

        [TestCase]
        public void TryExtractHashMarker_MalformedMarker_ReturnsFalse()
        {
            // Opening prefix but no closing bracket
            LogonTaskRegistration.TryExtractHashMarker("foo [gvfs-logon-task-hash=ABCD no close", out _).ShouldBeFalse();
            // Closing bracket before any content
            LogonTaskRegistration.TryExtractHashMarker("foo [gvfs-logon-task-hash=]", out _).ShouldBeFalse();
        }

        [TestCase]
        public void TryExtractHashMarker_FindsMarkerInGeneratedXml()
        {
            // Round-trip: the XML produced by BuildTaskXml must contain the
            // template hash, and TryExtractHashMarker must recover it.
            string xml = LogonTaskRegistration.BuildTaskXml(TestGvfsPath, TestUserSid);
            LogonTaskRegistration.TryExtractHashMarker(xml, out string hash).ShouldBeTrue();
            hash.ShouldEqual(LogonTaskRegistration.TemplateHash);
        }

        [TestCase]
        public void IsCurrent_NoRegisteredTask_ReturnsFalse()
        {
            MockScheduledTaskInvoker invoker = new MockScheduledTaskInvoker();
            // Default mock has no registered tasks => TryQueryXml fails.
            LogonTaskRegistration reg = new LogonTaskRegistration(new MockTracer(), invoker);
            reg.IsCurrent().ShouldBeFalse();
        }

        [TestCase]
        public void IsCurrent_MatchingHash_ReturnsTrue()
        {
            MockScheduledTaskInvoker invoker = new MockScheduledTaskInvoker();
            invoker.RegisteredTasks[LogonTaskRegistration.FullTaskPath] =
                LogonTaskRegistration.BuildTaskXml(TestGvfsPath, TestUserSid);
            LogonTaskRegistration reg = new LogonTaskRegistration(new MockTracer(), invoker);
            reg.IsCurrent().ShouldBeTrue();
        }

        [TestCase]
        public void IsCurrent_DifferentHash_ReturnsFalse()
        {
            MockScheduledTaskInvoker invoker = new MockScheduledTaskInvoker();
            // Simulate a task registered by a previous template version
            invoker.RegisteredTasks[LogonTaskRegistration.FullTaskPath] =
                "<Task><RegistrationInfo><Description>Old. [gvfs-logon-task-hash=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA]</Description></RegistrationInfo></Task>";
            LogonTaskRegistration reg = new LogonTaskRegistration(new MockTracer(), invoker);
            reg.IsCurrent().ShouldBeFalse();
        }

        [TestCase]
        public void IsCurrent_TaskExistsButNoMarker_ReturnsFalse()
        {
            MockScheduledTaskInvoker invoker = new MockScheduledTaskInvoker();
            invoker.RegisteredTasks[LogonTaskRegistration.FullTaskPath] =
                "<Task><RegistrationInfo><Description>Manually edited, no marker.</Description></RegistrationInfo></Task>";
            LogonTaskRegistration reg = new LogonTaskRegistration(new MockTracer(), invoker);
            reg.IsCurrent().ShouldBeFalse();
        }

        [TestCase]
        public void TryRegisterOrUpdate_CreatesNewTask()
        {
            MockScheduledTaskInvoker invoker = new MockScheduledTaskInvoker();
            LogonTaskRegistration reg = new LogonTaskRegistration(new MockTracer(), invoker);

            reg.TryRegisterOrUpdate(TestGvfsPath, TestUserSid, out string error).ShouldBeTrue(error);

            invoker.RegisteredTasks.ContainsKey(LogonTaskRegistration.FullTaskPath).ShouldBeTrue();
            invoker.RegisterCallCount.ShouldEqual(1);
        }

        [TestCase]
        public void TryRegisterOrUpdate_AlreadyCurrentSameArgs_NoRewrite()
        {
            MockScheduledTaskInvoker invoker = new MockScheduledTaskInvoker();
            invoker.RegisteredTasks[LogonTaskRegistration.FullTaskPath] =
                LogonTaskRegistration.BuildTaskXml(TestGvfsPath, TestUserSid);
            LogonTaskRegistration reg = new LogonTaskRegistration(new MockTracer(), invoker);

            reg.TryRegisterOrUpdate(TestGvfsPath, TestUserSid, out _).ShouldBeTrue();
            invoker.RegisterCallCount.ShouldEqual(0);
        }

        [TestCase]
        public void TryRegisterOrUpdate_CurrentHashButDifferentUser_Reregisters()
        {
            // Same template hash, but task is bound to a different user SID
            // (e.g. someone else's per-user task left behind). We must
            // re-register with the requesting user's SID.
            MockScheduledTaskInvoker invoker = new MockScheduledTaskInvoker();
            invoker.RegisteredTasks[LogonTaskRegistration.FullTaskPath] =
                LogonTaskRegistration.BuildTaskXml(TestGvfsPath, OtherUserSid);
            LogonTaskRegistration reg = new LogonTaskRegistration(new MockTracer(), invoker);

            reg.TryRegisterOrUpdate(TestGvfsPath, TestUserSid, out _).ShouldBeTrue();
            invoker.RegisterCallCount.ShouldEqual(1);
            invoker.RegisteredTasks[LogonTaskRegistration.FullTaskPath].Contains(TestUserSid).ShouldBeTrue();
            invoker.RegisteredTasks[LogonTaskRegistration.FullTaskPath].Contains(OtherUserSid).ShouldBeFalse();
        }

        [TestCase]
        public void TryRegisterOrUpdate_CurrentHashButDifferentGvfsPath_Reregisters()
        {
            // GVFS install moved (junction swap). Template hash unchanged
            // but the Command path in the task points at the old location.
            // We must rewrite.
            MockScheduledTaskInvoker invoker = new MockScheduledTaskInvoker();
            string oldPath = @"C:\Users\test\AppData\Local\Programs\GVFS\Versions\0.1.0\gvfs.exe";
            invoker.RegisteredTasks[LogonTaskRegistration.FullTaskPath] =
                LogonTaskRegistration.BuildTaskXml(oldPath, TestUserSid);
            LogonTaskRegistration reg = new LogonTaskRegistration(new MockTracer(), invoker);

            reg.TryRegisterOrUpdate(TestGvfsPath, TestUserSid, out _).ShouldBeTrue();
            invoker.RegisterCallCount.ShouldEqual(1);
            invoker.RegisteredTasks[LogonTaskRegistration.FullTaskPath].Contains(TestGvfsPath).ShouldBeTrue();
        }

        [TestCase]
        public void TryRegisterOrUpdate_InvokerFails_SurfacesError()
        {
            MockScheduledTaskInvoker invoker = new MockScheduledTaskInvoker();
            invoker.NextRegisterError = "Permission denied";
            LogonTaskRegistration reg = new LogonTaskRegistration(new MockTracer(), invoker);

            reg.TryRegisterOrUpdate(TestGvfsPath, TestUserSid, out string error).ShouldBeFalse();
            error.ShouldEqual("Permission denied");
        }

        [TestCase]
        public void TryUnregister_DelegatesToInvoker()
        {
            MockScheduledTaskInvoker invoker = new MockScheduledTaskInvoker();
            invoker.RegisteredTasks[LogonTaskRegistration.FullTaskPath] =
                LogonTaskRegistration.BuildTaskXml(TestGvfsPath, TestUserSid);
            LogonTaskRegistration reg = new LogonTaskRegistration(new MockTracer(), invoker);

            reg.TryUnregister(out string error).ShouldBeTrue(error);
            invoker.RegisteredTasks.ContainsKey(LogonTaskRegistration.FullTaskPath).ShouldBeFalse();
        }

        [TestCase]
        public void TryUnregister_TaskNotRegistered_StillReturnsTrue()
        {
            // Idempotent: unregister of nothing is a success.
            MockScheduledTaskInvoker invoker = new MockScheduledTaskInvoker();
            LogonTaskRegistration reg = new LogonTaskRegistration(new MockTracer(), invoker);

            reg.TryUnregister(out _).ShouldBeTrue();
        }

        [TestCase]
        public void Constructor_NullArgs_Throws()
        {
            MockScheduledTaskInvoker invoker = new MockScheduledTaskInvoker();
            Assert.Throws<ArgumentNullException>(() => new LogonTaskRegistration(null, invoker));
            Assert.Throws<ArgumentNullException>(() => new LogonTaskRegistration(new MockTracer(), null));
        }

        private sealed class MockScheduledTaskInvoker : IScheduledTaskInvoker
        {
            public Dictionary<string, string> RegisteredTasks { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public string NextRegisterError { get; set; }
            public string NextUnregisterError { get; set; }
            public int RegisterCallCount { get; private set; }

            public bool TryRegisterFromXml(string taskPath, string xml, out string errorMessage)
            {
                this.RegisterCallCount++;

                if (!string.IsNullOrEmpty(this.NextRegisterError))
                {
                    errorMessage = this.NextRegisterError;
                    return false;
                }

                this.RegisteredTasks[taskPath] = xml;
                errorMessage = string.Empty;
                return true;
            }

            public bool TryQueryXml(string taskPath, out string xml, out string errorMessage)
            {
                if (this.RegisteredTasks.TryGetValue(taskPath, out xml))
                {
                    errorMessage = string.Empty;
                    return true;
                }

                xml = null;
                errorMessage = "Task not found";
                return false;
            }

            public bool TryUnregister(string taskPath, out string errorMessage)
            {
                if (!string.IsNullOrEmpty(this.NextUnregisterError))
                {
                    errorMessage = this.NextUnregisterError;
                    return false;
                }

                this.RegisteredTasks.Remove(taskPath);
                errorMessage = string.Empty;
                return true;
            }
        }
    }
}
