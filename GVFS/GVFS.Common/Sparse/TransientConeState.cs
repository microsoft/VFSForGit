using System;
using System.Collections.Generic;

namespace GVFS.Common.Sparse
{
    /// <summary>
    /// The transient cone entries that Git commands added with a widen request, keyed by the
    /// process id of each Git command.
    /// </summary>
    /// <remarks>
    /// A widen adds the paths of one command. The narrow of the same command removes them. The
    /// cone is the union of the modified paths and the paths of every command that is still
    /// listed, so two commands that need the same directory do not remove it for each other.
    /// <para>
    /// A narrow can be missing (Git was stopped) or arrive before the widen that it pairs with
    /// (the hook stopped its wait, and Git ended first). For the first case, the owner removes
    /// the entries of processes that ended (see <see cref="RemoveEnded"/>). For the second case,
    /// a narrow for a command without entries records the command as narrowed, and
    /// <see cref="IsNarrowed"/> then refuses its late widen.
    /// </para>
    /// <para>
    /// A process id identifies a command only together with the process start time, because
    /// Windows reuses process ids. A null start time means that it was not available.
    /// </para>
    /// <para>
    /// This type does no I/O and is not thread safe. The owner serializes all calls.
    /// </para>
    /// </remarks>
    public class TransientConeState
    {
        private readonly Dictionary<int, Command> commands = new Dictionary<int, Command>();
        private readonly Dictionary<int, long?> narrowedCommands = new Dictionary<int, long?>();

        /// <summary>Returns whether a process still runs: (process id, start time) -> running.</summary>
        public delegate bool IsProcessRunning(int processId, long? startTime);

        /// <summary>The number of commands that have transient entries.</summary>
        public int CommandCount => this.commands.Count;

        /// <summary>
        /// Return true when the command already sent its narrow. A start time that differs from
        /// the recorded one belongs to another process with the same id.
        /// </summary>
        public bool IsNarrowed(int processId, long? startTime)
        {
            return this.narrowedCommands.TryGetValue(processId, out long? narrowedStartTime) &&
                IsSameProcess(narrowedStartTime, startTime);
        }

        /// <summary>
        /// Add the paths of a command. A repeated widen of the same command keeps the paths of
        /// both requests. An entry with the same process id and another start time belongs to a
        /// process that ended, so the new command replaces it.
        /// </summary>
        public void Add(int processId, long? startTime, IEnumerable<string> paths)
        {
            ArgumentNullException.ThrowIfNull(paths);

            if (!this.commands.TryGetValue(processId, out Command command) ||
                !IsSameProcess(command.StartTime, startTime))
            {
                command = new Command(startTime);
                this.commands[processId] = command;
            }

            this.narrowedCommands.Remove(processId);
            foreach (string path in paths)
            {
                if (!string.IsNullOrEmpty(path))
                {
                    command.Paths.Add(path);
                }
            }
        }

        /// <summary>
        /// Remove the paths of a command. When the command has no entries and its start time is
        /// known, record it as narrowed so that a late widen does not add entries that no narrow
        /// removes. Without a start time, a later process with the same id cannot be told apart,
        /// so nothing is recorded.
        /// </summary>
        /// <returns>True when the command had entries.</returns>
        public bool Remove(int processId, long? startTime)
        {
            if (this.commands.Remove(processId))
            {
                return true;
            }

            if (startTime != null)
            {
                this.narrowedCommands[processId] = startTime;
            }

            return false;
        }

        /// <summary>
        /// Remove the entries and the narrowed records of every process that no longer runs.
        /// </summary>
        /// <returns>The number of commands whose entries were removed.</returns>
        public int RemoveEnded(IsProcessRunning isRunning)
        {
            ArgumentNullException.ThrowIfNull(isRunning);

            List<int> ended = new List<int>();
            foreach (KeyValuePair<int, Command> command in this.commands)
            {
                if (!isRunning(command.Key, command.Value.StartTime))
                {
                    ended.Add(command.Key);
                }
            }

            foreach (int processId in ended)
            {
                this.commands.Remove(processId);
            }

            List<int> endedNarrowed = new List<int>();
            foreach (KeyValuePair<int, long?> narrowed in this.narrowedCommands)
            {
                if (!isRunning(narrowed.Key, narrowed.Value))
                {
                    endedNarrowed.Add(narrowed.Key);
                }
            }

            foreach (int processId in endedNarrowed)
            {
                this.narrowedCommands.Remove(processId);
            }

            return ended.Count;
        }

        /// <summary>The union of the paths of all commands, in no specific order.</summary>
        public IEnumerable<string> GetAllPaths()
        {
            HashSet<string> union = new HashSet<string>(StringComparer.Ordinal);
            foreach (Command command in this.commands.Values)
            {
                union.UnionWith(command.Paths);
            }

            return union;
        }

        private static bool IsSameProcess(long? recordedStartTime, long? startTime)
        {
            // Without both start times, only the process id is known. Treat it as the same
            // process: the result is a refused widen or a kept entry, which is safe.
            return recordedStartTime == null || startTime == null || recordedStartTime == startTime;
        }

        private class Command
        {
            public Command(long? startTime)
            {
                this.StartTime = startTime;
            }

            public long? StartTime { get; }

            public HashSet<string> Paths { get; } = new HashSet<string>(StringComparer.Ordinal);
        }
    }
}
