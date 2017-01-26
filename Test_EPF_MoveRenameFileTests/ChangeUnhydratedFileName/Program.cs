using NUnitLite;
using System;
using System.Threading;

namespace GVFS.StressTests
{
    public class Program
    {
        public static void Main(string[] args)
        {
            string[] test_args = args;

            for (int i = 0; i < Properties.Settings.Default.TestRepeatCount; i++)
            {
                Console.WriteLine("Starting pass {0}", i + 1);
                DateTime now = DateTime.Now;
                new AutoRun().Execute(test_args);
                Console.WriteLine("Completed pass {0} in {1}", i + 1, DateTime.Now - now);
                Console.WriteLine();

                Thread.Sleep(TimeSpan.FromSeconds(1));
            }

            Console.WriteLine("All tests completed.  Press Enter to exit.");
            Console.ReadLine();
        }
    }
}