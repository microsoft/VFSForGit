using GVFS.FunctionalTests.Tools;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.FunctionalTests.Tests
{
    [TestFixture]
    [NonParallelizable]
    public class InstallationTelemetryTests
    {
        [Test]
        public async Task ConsoleServiceDeliversLateRecordsAndReplaysAfterRestart()
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                nameof(InstallationTelemetryTests) + Guid.NewGuid().ToString("N"));
            string name = "Test.GVFS.InstallationTelemetry." + Guid.NewGuid().ToString("N");
            string recordDirectory = Path.Combine(root, "AppData", name, "InstallationTelemetry");
            Directory.CreateDirectory(root);
            string systemConfig = Path.Combine(root, "system.gitconfig");
            string globalConfig = Path.Combine(root, "global.gitconfig");
            File.WriteAllText(systemConfig, string.Empty);
            File.WriteAllText(globalConfig, string.Empty);
            Process service = null;
            try
            {
                service = StartService(root, name, systemConfig, globalConfig);
                using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
                {
                    string pipeName = "GVFS.FunctionalTests.Telemetry." + Guid.NewGuid().ToString("N");
                    Task<string> received = ReadInstallationMessage(pipeName, timeout.Token);
                    TestRecord record = new TestRecord();
                    Publish(recordDirectory, record);
                    File.WriteAllText(systemConfig, "[gvfs]\ntelemetry-pipe = " + pipeName + "\n");
                    VerifyMessage(await received, record);
                    Assert.IsTrue(SpinWait.SpinUntil(
                        () => !File.Exists(Path.Combine(recordDirectory, record.EventId + ".json")),
                        TimeSpan.FromSeconds(10)));
                }

                TestRecord replay = new TestRecord();
                Publish(recordDirectory, replay);
                string replayPath = Path.Combine(recordDirectory, replay.EventId + ".json");
                Assert.IsTrue(File.Exists(replayPath));
                service.Kill(entireProcessTree: true);
                await service.WaitForExitAsync();
                service.Dispose();
                service = null;

                Assert.IsTrue(File.Exists(replayPath));
                string restartPipe = "GVFS.FunctionalTests.Telemetry." + Guid.NewGuid().ToString("N");
                File.WriteAllText(systemConfig, "[gvfs]\ntelemetry-pipe = " + restartPipe + "\n");
                using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                {
                    Task<string> received = ReadInstallationMessage(restartPipe, timeout.Token);
                    service = StartService(root, name, systemConfig, globalConfig);
                    VerifyMessage(await received, replay);
                    Assert.IsFalse(service.HasExited);
                    Assert.IsTrue(SpinWait.SpinUntil(() => !File.Exists(replayPath), TimeSpan.FromSeconds(10)));
                }
            }
            finally
            {
                if (service != null)
                {
                    if (!service.HasExited)
                    {
                        service.Kill(entireProcessTree: true);
                        await service.WaitForExitAsync();
                    }

                    service.Dispose();
                }

                Directory.Delete(root, recursive: true);
            }
        }

        private static Process StartService(string root, string name, string systemConfig, string globalConfig)
        {
            return GVFSServiceProcess.StartConsoleService(name, new Dictionary<string, string>
            {
                { "GVFS_COMMON_APPDATA_ROOT", Path.Combine(root, "AppData") },
                { "GVFS_SECURE_DATA_ROOT", Path.Combine(root, "Secure") },
                { "GIT_CONFIG_SYSTEM", systemConfig },
                { "GIT_CONFIG_GLOBAL", globalConfig },
                { "GIT_CONFIG_COUNT", "0" },
                { "HOME", root },
            });
        }

        private static NamedPipeServerStream CreateCollector(string name)
        {
            return new NamedPipeServerStream(name, PipeDirection.In, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        }

        private static async Task<string> ReadInstallationMessage(string name, CancellationToken token)
        {
            using (CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (NamedPipeServerStream first = CreateCollector(name))
            using (NamedPipeServerStream second = CreateCollector(name))
            {
                Task<string>[] readers = new Task<string>[] { ReadMessage(first, stop.Token), ReadMessage(second, stop.Token) };
                string result = await await Task.WhenAny(readers);
                stop.Cancel();
                try
                {
                    await Task.WhenAll(readers);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                {
                }

                return result;
            }
        }

        private static async Task<string> ReadMessage(NamedPipeServerStream server, CancellationToken token)
        {
            await server.WaitForConnectionAsync(token);
            using (StreamReader reader = new StreamReader(server, Encoding.UTF8, false, 1024, leaveOpen: true))
            {
                string line;
                while ((line = await reader.ReadLineAsync(token)) != null)
                {
                    using (JsonDocument message = JsonDocument.Parse(line))
                    {
                        if (message.RootElement.GetProperty("eventName").GetString() == "InstallationStarted")
                        {
                            return line;
                        }
                    }
                }

                throw new IOException("The collector connection closed before an installation event.");
            }
        }

        private static void VerifyMessage(string json, TestRecord expected)
        {
            using (JsonDocument message = JsonDocument.Parse(json))
            using (JsonDocument payload = JsonDocument.Parse(message.RootElement.GetProperty("payload").GetProperty("json").GetString()))
            {
                Assert.AreEqual("InstallationStarted", message.RootElement.GetProperty("eventName").GetString());
                JsonElement actual = payload.RootElement;
                Assert.AreEqual(expected.EventId, actual.GetProperty("EventId").GetGuid());
                Assert.AreEqual(expected.AttemptId, actual.GetProperty("AttemptId").GetGuid());
                Assert.AreEqual(DateTimeOffset.Parse(expected.OccurredUtc), actual.GetProperty("OccurredUtc").GetDateTimeOffset());
                Assert.AreEqual("0.2.1.0", actual.GetProperty("InstallerVersion").GetString());
                Assert.AreEqual("arm64", actual.GetProperty("PackageArchitecture").GetString());
            }
        }

        private static void Publish(string directory, TestRecord record)
        {
            DirectorySecurity security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                security.SetOwner(identity.User);
                security.AddAccessRule(new FileSystemAccessRule(
                    identity.User, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None, AccessControlType.Allow));
            }

            security.CreateDirectory(directory);
            string path = Path.Combine(directory, record.EventId + ".json");
            string json = "{\"SchemaVersion\":1,\"AttemptId\":\"" + record.AttemptId +
                "\",\"EventId\":\"" + record.EventId + "\",\"EventType\":\"InstallationStarted\",\"OccurredUtc\":\"" + record.OccurredUtc +
                "\",\"InstallerVersion\":\"0.2.1.0\",\"PreviousVersion\":\"Unknown\",\"PackageArchitecture\":\"arm64\",\"OSArchitecture\":\"arm64\"," +
                "\"Mode\":\"Fresh\",\"Outcome\":\"Started\",\"LastPhase\":\"Prepare\",\"RemountOutcome\":\"NotRequested\",\"ScriptResult\":0}";
            File.WriteAllText(path + ".tmp", json);
            File.Move(path + ".tmp", path);
        }

        private sealed class TestRecord
        {
            public Guid AttemptId { get; } = Guid.NewGuid();
            public Guid EventId { get; } = Guid.NewGuid();
            public string OccurredUtc { get; } = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
        }
    }
}
