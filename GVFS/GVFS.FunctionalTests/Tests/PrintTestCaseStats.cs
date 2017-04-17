using NUnit.Framework;
using NUnit.Framework.Interfaces;
using System;

[assembly: GVFS.FunctionalTests.Tests.PrintTestCaseStats]

namespace GVFS.FunctionalTests.Tests
{
    public class PrintTestCaseStats : TestActionAttribute
    {
        public override ActionTargets Targets
        {
            get { return ActionTargets.Test; }
        }

        public override void BeforeTest(ITest test)
        {
            Console.WriteLine("Test " + test.FullName.Substring("GVFS.FunctionalTests.Tests.".Length));
            Console.WriteLine("Started at   {0:hh:mm:ss}", DateTime.Now);
        }

        public override void AfterTest(ITest test)
        {
            Console.WriteLine("Completed at {0:hh:mm:ss}", DateTime.Now);

            string message = TestContext.CurrentContext.Result.Message;
            TestStatus status = TestContext.CurrentContext.Result.Outcome.Status;
            
            Console.WriteLine("Status:      {0}\n", status.ToString());
            if (status != TestStatus.Passed)
            {
                Console.WriteLine(message);
            }
        }
    }
}