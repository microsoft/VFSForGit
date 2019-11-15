using GVFS.Common.NamedPipes;
using GVFS.Service.UI;
using GVFS.UnitTests.Mock.Common;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GVFS.UnitTests.Windows.ServiceUI
{
    [TestFixture]
    public class GVFSToastRequestHandlerTests
    {
        private NamedPipeMessages.Notification.Request request;
        private GVFSToastRequestHandler toastHandler;
        private Mock<IToastNotifier> mockToastNotifier;
        private MockTracer tracer;

        [SetUp]
        public void Setup()
        {
            this.tracer = new MockTracer();
            this.mockToastNotifier = new Mock<IToastNotifier>(MockBehavior.Strict);
            this.mockToastNotifier.SetupSet(toastNotifier => toastNotifier.UserResponseCallback = It.IsAny<Action<string>>()).Verifiable();
            this.toastHandler = new GVFSToastRequestHandler(this.mockToastNotifier.Object, this.tracer);
            this.request = new NamedPipeMessages.Notification.Request();
        }

        [TestCase]
        public void UpgradeToastIsActionableAndContainsVersionInfo()
        {
            const string version = "1.0.956749.2";

            this.request.Id = NamedPipeMessages.Notification.Request.Identifier.UpgradeAvailable;
            this.request.NewVersion = version;

            this.VerifyToastMessage(
                expectedTitle: "New version " + version + " is available",
                expectedMessage: "click Upgrade button",
                expectedButtonTitle: "Upgrade",
                expectedGVFSCmd: "gvfs upgrade --confirm");
        }

        [TestCase]
        public void MountFailureToastIsActionableAndContainEnlistmentInfo()
        {
            const string enlistmentRoot = "D:\\Work\\OS";

            this.request.Id = NamedPipeMessages.Notification.Request.Identifier.MountFailure;
            this.request.Enlistment = enlistmentRoot;

            this.VerifyToastMessage(
                expectedTitle: "VFS For Git Automount",
                expectedMessage: enlistmentRoot,
                expectedButtonTitle: "Retry",
                expectedGVFSCmd: "gvfs mount " + enlistmentRoot);
        }

        [TestCase]
        public void MountStartIsNotActionableAndContainsEnlistmentCount()
        {
            const int enlistmentCount = 10;

            this.request.Id = NamedPipeMessages.Notification.Request.Identifier.AutomountStart;
            this.request.EnlistmentCount = enlistmentCount;

            this.VerifyToastMessage(
                expectedTitle: "VFS For Git Automount",
                expectedMessage: "mount " + enlistmentCount.ToString() + " VFS For Git repos",
                expectedButtonTitle: null,
                expectedGVFSCmd: null);
        }

        [TestCase]
        public void UnknownToastRequestGetsIgnored()
        {
            this.request.Id = (NamedPipeMessages.Notification.Request.Identifier)10;
            this.request.EnlistmentCount = 232;
            this.request.Enlistment = "C:\\OS";

            this.toastHandler.HandleToastRequest(this.tracer, this.request);

            this.mockToastNotifier.Verify(
                toastNotifier => toastNotifier.Notify(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()),
                Times.Never());
        }

        private void VerifyToastMessage(
            string expectedTitle,
            string expectedMessage,
            string expectedButtonTitle,
            string expectedGVFSCmd)
        {
            this.mockToastNotifier.Setup(toastNotifier => toastNotifier.Notify(
                expectedTitle,
                It.Is<string>(message => message.Contains(expectedMessage)),
                expectedButtonTitle,
                expectedGVFSCmd));

            this.toastHandler.HandleToastRequest(this.tracer, this.request);
            this.mockToastNotifier.VerifyAll();
        }
    }
}
