using GVFS.Common;
using GVFS.Platform.Windows;
using GVFS.Virtualization.Projection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace GVFS.PerfProfiling
{
    internal class Program
    {
        [Flags]
        private enum TestsToRun
        {
            ValidateIndex = 1 << 0,
            RebuildProjection = 1 << 1,
            ValidateModifiedPaths = 1 << 2,
            All = -1,
        }

        private static void Main(string[] args)
        {
            GVFSPlatform.Register(new WindowsPlatform());
            string enlistmentRootPath = @"M:\OS";
            if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
            {
                enlistmentRootPath = args[0];
            }

            TestsToRun testsToRun = TestsToRun.All;
            if (args.Length > 1)
            {
                int tests;
                if (int.TryParse(args[1], out tests))
                {
                    testsToRun = (TestsToRun)tests;
                }
            }

            ProfilingEnvironment environment = new ProfilingEnvironment(enlistmentRootPath);

            Dictionary<TestsToRun, Action> allTests = new Dictionary<TestsToRun, Action>
            {
                { TestsToRun.ValidateIndex, () => GitIndexProjection.ReadIndex(environment.Context.Tracer, Path.Combine(environment.Enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Index)) },
                { TestsToRun.RebuildProjection, () => environment.FileSystemCallbacks.GitIndexProjectionProfiler.ForceRebuildProjection() },
                { TestsToRun.ValidateModifiedPaths, () => environment.FileSystemCallbacks.GitIndexProjectionProfiler.ForceAddMissingModifiedPaths(environment.Context.Tracer) },
            };

            long before = GetMemoryUsage();

            foreach (KeyValuePair<TestsToRun, Action> test in allTests)
            {
                if (IsOn(testsToRun, test.Key))
                {
                    TimeIt(test.Key.ToString(), test.Value);
                }
            }

            long after = GetMemoryUsage();

            Console.WriteLine($"Memory Usage: {FormatByteCount(after - before)}");
            Console.WriteLine();
            Console.WriteLine("Press Enter to exit");
            Console.Read();
        }

        private static bool IsOn(TestsToRun value, TestsToRun flag)
        {
            return flag == (value & flag);
        }

        private static void TimeIt(string name, Action action)
        {
            List<TimeSpan> times = new List<TimeSpan>();
            const int runs = 10;

            Console.WriteLine();
            Console.WriteLine($"Measuring {name}:");
            Console.WriteLine();

            for (int i = 0; i < runs + 1; i++)
            {
                long before = GetMemoryUsage();

                Stopwatch stopwatch = Stopwatch.StartNew();
                action();
                stopwatch.Stop();

                long after = GetMemoryUsage();

                times.Add(stopwatch.Elapsed);
                Console.WriteLine($"Time: {stopwatch.Elapsed.TotalMilliseconds} ms");
                Console.WriteLine($"New allocations: {FormatByteCount(after - before)}");
            }

            Console.WriteLine();
            Console.WriteLine($"Average Time {runs} runs - {name} {times.Select(timespan => timespan.TotalMilliseconds).Skip(1).Average()} ms");
            Console.WriteLine("----------------------------");
        }

        private static long GetMemoryUsage()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect(generation: 2, mode: GCCollectionMode.Forced, blocking: true, compacting: true);
            using (Process process = Process.GetCurrentProcess())
            {
                return process.PrivateMemorySize64;
            }
        }

        private static string FormatByteCount(long byteCount)
        {
            const int Divisor = 1024;
            string[] unitStrings = { " B", " KB", " MB", " GB", " TB" };

            int unitIndex = 0;

            bool isNegative = false;
            if (byteCount < 0)
            {
                isNegative = true;
                byteCount *= -1;
            }

            while (byteCount >= Divisor && unitIndex < unitStrings.Length - 1)
            {
                unitIndex++;
                byteCount = byteCount / Divisor;
            }

            return (isNegative ? "-" : string.Empty) + byteCount.ToString("N0") + unitStrings[unitIndex];
        }
    }
}
