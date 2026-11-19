using GVFS.Common.Sparse;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace GVFS.UnitTests.Common.Sparse
{
    [TestFixture]
    public class TransientConeStateTests
    {
        [TestCase]
        public void PathsOfAllCommandsAreKeptUntilEachCommandIsRemoved()
        {
            TransientConeState state = new TransientConeState();
            state.Add(100, startTime: 1, new[] { "A/a.txt", "B/" });
            state.Add(200, startTime: 2, new[] { "A/a.txt", "C/c.txt" });

            Sorted(state.GetAllPaths()).ShouldMatchInOrder("A/a.txt", "B/", "C/c.txt");

            state.Remove(100, startTime: 1).ShouldBeTrue();
            Sorted(state.GetAllPaths()).ShouldMatchInOrder("A/a.txt", "C/c.txt");

            state.Remove(200, startTime: 2).ShouldBeTrue();
            state.GetAllPaths().ShouldBeEmpty();
            state.CommandCount.ShouldEqual(0);
        }

        [TestCase]
        public void RepeatedAddOfOneCommandKeepsThePathsOfBothRequests()
        {
            TransientConeState state = new TransientConeState();
            state.Add(100, startTime: 1, new[] { "A/a.txt" });
            state.Add(100, startTime: 1, new[] { "B/b.txt" });

            Sorted(state.GetAllPaths()).ShouldMatchInOrder("A/a.txt", "B/b.txt");
            state.CommandCount.ShouldEqual(1);
        }

        [TestCase]
        public void AddForAReusedProcessIdReplacesThePathsOfTheEndedProcess()
        {
            TransientConeState state = new TransientConeState();
            state.Add(100, startTime: 1, new[] { "A/a.txt" });
            state.Add(100, startTime: 2, new[] { "B/b.txt" });

            state.GetAllPaths().ShouldMatchInOrder("B/b.txt");
        }

        [TestCase]
        public void RemoveOfACommandWithoutPathsRecordsItAsNarrowed()
        {
            TransientConeState state = new TransientConeState();

            state.Remove(100, startTime: 1).ShouldBeFalse();

            state.IsNarrowed(100, startTime: 1).ShouldBeTrue();
            state.IsNarrowed(100, startTime: null).ShouldBeTrue();
            state.IsNarrowed(100, startTime: 2).ShouldBeFalse("Another process has this id");
            state.IsNarrowed(200, startTime: 1).ShouldBeFalse();
        }

        [TestCase]
        public void RemoveWithoutAStartTimeRecordsNoNarrowedCommand()
        {
            TransientConeState state = new TransientConeState();

            state.Remove(100, startTime: null).ShouldBeFalse();

            state.IsNarrowed(100, startTime: 1).ShouldBeFalse();
        }

        [TestCase]
        public void RemoveEndedRemovesThePathsAndNarrowedRecordsOfEndedProcesses()
        {
            TransientConeState state = new TransientConeState();
            state.Add(100, startTime: 1, new[] { "A/a.txt" });
            state.Add(200, startTime: 2, new[] { "B/b.txt" });
            state.Remove(300, startTime: 3);
            HashSet<int> running = new HashSet<int> { 200 };

            state.RemoveEnded((processId, startTime) => running.Contains(processId)).ShouldEqual(1);

            state.GetAllPaths().ShouldMatchInOrder("B/b.txt");
            state.IsNarrowed(300, startTime: 3).ShouldBeFalse();
        }

        private static IEnumerable<string> Sorted(IEnumerable<string> paths)
        {
            return paths.OrderBy(path => path, System.StringComparer.Ordinal);
        }
    }
}
