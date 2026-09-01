using GVFS.Common;
using GVFS.Common.NamedPipes;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System;
using System.IO.Pipes;
using System.Threading.Tasks;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class WaitUntilMountedFailureReportingTests
    {
        private const string EnlistmentRoot = "C:\\fake\\root";

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
        /// <remarks>
        /// Uses a raw <see cref="NamedPipeServerStream"/> rather than
        /// <see cref="NamedPipeServer"/>, because the latter goes through
        /// GVFSPlatform.CreatePipeByName, which the unit-test mock platform does not
        /// support.
        /// </remarks>
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

            using (NamedPipeServerStream serverStream = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1))
            {
                Task serverTask = Task.Run(() =>
                {
                    serverStream.WaitForConnection();

                    NamedPipeStreamReader reader = new NamedPipeStreamReader(serverStream);
                    NamedPipeStreamWriter writer = new NamedPipeStreamWriter(serverStream);

                    // WaitUntilMounted sends GetStatus and stops on the first
                    // MountFailed response, so one exchange is enough.
                    reader.ReadMessage();
                    writer.WriteMessage(responseJson);
                });

                try
                {
                    string errorMessage;
                    bool result = GVFSEnlistment.WaitUntilMounted(
                        new MockTracer(),
                        pipeName,
                        EnlistmentRoot,
                        unattended: false,
                        out errorMessage);

                    result.ShouldBeFalse();
                    return errorMessage;
                }
                finally
                {
                    serverTask.Wait(TimeSpan.FromSeconds(5));
                }
            }
        }
    }
}
