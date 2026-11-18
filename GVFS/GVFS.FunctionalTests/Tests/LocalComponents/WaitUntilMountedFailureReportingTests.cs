using GVFS.Common;
using GVFS.Common.NamedPipes;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.FunctionalTests.Tests.LocalComponents
{
    [TestFixture]
    [NonParallelizable]
    public class WaitUntilMountedFailureReportingTests
    {
        private const string EnlistmentRoot = "mock:\\fake\\root";

        [TestCase]
        public void ReportsTheMountErrorSentByTheMountProcess()
        {
            const string MountError =
                "Error: Failed to start virtualization instance (-2147024418). "
                + "The ProjFS filter (PrjFlt) cannot attach to this volume.";

            string errorMessage = RunAgainstMountFailedServer(MountError);

            errorMessage.ShouldEqual(MountError);
        }

        [TestCase]
        public void FallsBackToGenericMessageWhenNoMountErrorIsSent()
        {
            string errorMessage = RunAgainstMountFailedServer(mountError: null);

            errorMessage.ShouldEqual("Failed to mount at " + EnlistmentRoot);
        }

        /// <summary>
        /// Stands in for GVFS.Mount: answers a single GetStatus request with MountFailed,
        /// then returns the error message WaitUntilMounted produced.
        /// </summary>
        private static string RunAgainstMountFailedServer(string mountError)
        {
            string pipeName = "GVFS_test_mount_failed_" + Guid.NewGuid().ToString("N");

            NamedPipeMessages.GetStatus.Response response = new NamedPipeMessages.GetStatus.Response
            {
                MountStatus = NamedPipeMessages.GetStatus.MountFailed,
                MountError = mountError,
                EnlistmentRoot = EnlistmentRoot,
            };

            string responseJson = response.ToJson();

            using (CancellationTokenSource deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            using (NamedPipeServerStream serverStream = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous))
            {
                using CancellationTokenRegistration abort = deadline.Token.Register(() => serverStream.Dispose());
                Task serverTask = Task.Run(async () =>
                {
                    await serverStream.WaitForConnectionAsync(deadline.Token);

                    NamedPipeStreamReader reader = new NamedPipeStreamReader(serverStream);
                    NamedPipeStreamWriter writer = new NamedPipeStreamWriter(serverStream);

                    reader.ReadMessage();
                    writer.WriteMessage(responseJson);
                });

                try
                {
                    using MockTracer tracer = new MockTracer();
                    string errorMessage;
                    bool result = GVFSEnlistment.WaitUntilMounted(
                        tracer,
                        pipeName,
                        EnlistmentRoot,
                        unattended: false,
                        out errorMessage);

                    result.ShouldBeFalse();
                    serverTask.Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue(
                        "The stand-in mount server must answer and complete.");
                    return errorMessage;
                }
                finally
                {
                    deadline.Cancel();
                    try
                    {
                        serverTask.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                    }
                    catch (OperationCanceledException) when (deadline.IsCancellationRequested)
                    {
                    }
                    catch (ObjectDisposedException) when (deadline.IsCancellationRequested)
                    {
                    }
                    catch (IOException) when (deadline.IsCancellationRequested)
                    {
                    }
                }
            }
        }
    }
}
