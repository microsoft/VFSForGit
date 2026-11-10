using GVFS.Common.NamedPipes;
using GVFS.UnitTests.Category;
using NUnit.Framework;
using System;

namespace GVFS.FunctionalTests.Tests.LocalComponents
{
    [TestFixture]
    [NonParallelizable]
    public class HydrationStatusDiagnosticsTests
    {
        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void RealPipeConnectionReturnsItsTimeoutCause()
        {
            using (NamedPipeClient client = new NamedPipeClient("GVFS.FunctionalTests.Missing." + Guid.NewGuid().ToString("N")))
            {
                Assert.IsFalse(client.Connect(25, out Exception error));
                Assert.IsInstanceOf<TimeoutException>(error);
            }
        }
    }
}
