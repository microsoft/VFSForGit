using NUnitLite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace GVFS.Tests
{
    public class NUnitRunner
    {
        private List<string> args;

        public NUnitRunner(string[] args)
        {
            this.args = new List<string>(args);
        }

        public string GetCustomArgWithParam(string arg)
        {
            string match = this.args.Where(a => a.StartsWith(arg + "=")).SingleOrDefault();
            if (match == null)
            {
                return null;
            }

            this.args.Remove(match);
            return match.Substring(arg.Length + 1);
        }

        public bool HasCustomArg(string arg)
        {
            // We also remove it as we're checking, because nunit wouldn't understand what it means
            return this.args.Remove(arg);
        }

        public void AddGlobalSetupIfNeeded(string globalSetup)
        {
            // If there are any test filters, the GlobalSetup still needs to run so add it.
            if (this.args.Any(x => x.StartsWith("--test=")))
            {
                this.args.Add($"--test={globalSetup}");
            }
        }

        public void PrepareTestSlice(string filters, (uint, uint) testSlice)
        {
            IEnumerable<string> args = this.args.Concat(new[] { "--explore" });
            if (filters.Length > 0)
            {
                args = args.Concat(new[] { "--where", filters });
            }

            // Temporarily redirect Console.Out to capture the output of --explore
            var stringWriter = new StringWriter();
            var originalOut = Console.Out;

            string[] list;
            try
            {
                Console.SetOut(stringWriter);
                int exploreResult = new AutoRun(Assembly.GetEntryAssembly()).Execute(args.ToArray());
                if (exploreResult != 0)
                {
                    throw new Exception("--explore failed with " + exploreResult);
                }

                list = stringWriter.ToString().Split(new[] { "\n" }, StringSplitOptions.None);
            }
            finally
            {
                Console.SetOut(originalOut); // Ensure we restore Console.Out
            }

            // Sort the test cases into roughly equal-sized buckets;
            // Care must be taken to ensure that all test cases for a given
            // EnlistmentPerFixture class go into the same bucket, as they
            // may very well be dependent on each other.

            // First, create the buckets
            List<string>[] buckets = new List<string>[testSlice.Item2];
            // There is no PriorityQueue in .NET Framework; Emulate one via
            // a sorted set that contains tuples of (bucket index, bucket size).
            var priorityQueue = new SortedSet<(int, int)>(
                    Comparer<(int, int)>.Create((x, y) =>
                    {
                        if (x.Item2 != y.Item2)
                        {
                            return x.Item2.CompareTo(y.Item2);
                        }
                        return x.Item1.CompareTo(y.Item1);
                    }));
            for (int i = 0; i < buckets.Length; i++)
            {
                buckets[i] = new List<string>();
                priorityQueue.Add((i, buckets[i].Count));
            }

            // Now distribute the tests into the buckets
            Regex perFixtureRegex = new Regex(
                @"^.*\.EnlistmentPerFixture\..+\.",
                // @"^.*\.",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            for (uint i = 0; i < list.Length; i++)
            {
                var test = list[i].Trim();
                if (!test.StartsWith("GVFS.")) continue;

                var bucket = priorityQueue.Min;
                priorityQueue.Remove(bucket);

                buckets[bucket.Item1].Add(test);

                // Ensure that EnlistmentPerFixture tests of the same class are all in the same bucket
                var match = perFixtureRegex.Match(test);
                if (match.Success)
                {
                    string prefix = match.Value;
                    while (i + 1 < list.Length && list[i + 1].StartsWith(prefix))
                    {
                        buckets[bucket.Item1].Add(list[++i].Trim());
                    }
                }

                bucket.Item2 = buckets[bucket.Item1].Count;
                priorityQueue.Add(bucket);
            }

            // Write the respective bucket's contents to a file
            string listFile = $"GVFS_test_slice_{testSlice.Item1}_of_{testSlice.Item2}.txt";
            File.WriteAllLines(listFile, buckets[testSlice.Item1]);
            Console.WriteLine($"Wrote {buckets[testSlice.Item1].Count} test cases to {listFile}");

            this.args.Add($"--testlist={listFile}");
        }

        public int RunTests(ICollection<string> includeCategories, ICollection<string> excludeCategories, (uint, uint)? testSlice = null)
        {
            string filters = GetFiltersArgument(includeCategories, excludeCategories);

            if (testSlice.HasValue && testSlice.Value.Item2 != 1)
            {
                this.PrepareTestSlice(filters, testSlice.Value);
            }
            else if (filters.Length > 0)
            {
                this.args.Add("--where");
                this.args.Add(filters);
            }

            DateTime now = DateTime.Now;
            int result = new AutoRun(Assembly.GetEntryAssembly()).Execute(this.args.ToArray());

            Console.WriteLine("Completed test pass in {0}", DateTime.Now - now);
            Console.WriteLine();

            return result;
        }

        private static string GetFiltersArgument(ICollection<string> includeCategories, ICollection<string> excludeCategories)
        {
            string filters = string.Empty;
            if (includeCategories != null && includeCategories.Any())
            {
                filters = "(" + string.Join("||", includeCategories.Select(x => $"cat=={x}")) + ")";
            }

            if (excludeCategories != null && excludeCategories.Any())
            {
                filters += (filters.Length > 0 ? "&&" : string.Empty) + string.Join("&&", excludeCategories.Select(x => $"cat!={x}"));
            }

            return filters;
        }
    }
}
