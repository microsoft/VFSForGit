using NUnit.Framework;
using NUnit.Framework.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

[assembly: GVFS.FunctionalTests.Tests.PrintTestCaseStats]

namespace GVFS.FunctionalTests.Tests
{
    public class PrintTestCaseStats : TestActionAttribute
    {
        private const string StartTimeKey = "StartTime";

        private static ConcurrentDictionary<string, TimeSpan> fixtureRunTimes = new ConcurrentDictionary<string, TimeSpan>();
        private static ConcurrentDictionary<string, TimeSpan> testRunTimes = new ConcurrentDictionary<string, TimeSpan>();

        public override ActionTargets Targets
        {
            get { return ActionTargets.Test; }
        }

        public static void PrintRunTimeStats()
        {
            Console.WriteLine();
            Console.WriteLine("Fixture run times:");
            foreach (KeyValuePair<string, TimeSpan> fixture in fixtureRunTimes.OrderByDescending(kvp => kvp.Value))
            {
                Console.WriteLine("    {0}\t{1}", fixture.Value, fixture.Key);
            }

            Console.WriteLine();
            Console.WriteLine("Test case run times:");
            foreach (KeyValuePair<string, TimeSpan> testcase in testRunTimes.OrderByDescending(kvp => kvp.Value))
            {
                Console.WriteLine("    {0}\t{1}", testcase.Value, testcase.Key);
            }
        }

        public override void BeforeTest(ITest test)
        {
            test.Properties.Add(StartTimeKey, DateTime.Now);
        }

        public override void AfterTest(ITest test)
        {
            DateTime startTime = (DateTime)test.Properties.Get(StartTimeKey);
            DateTime endTime = DateTime.Now;
            TimeSpan duration = endTime - startTime;
            string message = TestContext.CurrentContext.Result.Message;
            TestStatus status = TestContext.CurrentContext.Result.Outcome.Status;

            Console.WriteLine("Test " + test.FullName);
            Console.WriteLine($"{status} at {endTime.ToLongTimeString()} taking {duration}");
            if (status != TestStatus.Passed)
            {
                Console.WriteLine(message);
            }

            Console.WriteLine();

            fixtureRunTimes.AddOrUpdate(
                test.ClassName,
                duration,
                (key, existingValue) => existingValue + duration);
            testRunTimes.TryAdd(test.FullName, duration);
        }
    }
}