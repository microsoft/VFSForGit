using GVFS.Common;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace GVFS.Platform.Windows
{
    internal sealed class PlaceholderRequestProcessTree
    {
        private const int MaximumCachedProcesses = 64;
        private const uint SnapshotProcesses = 2;
        private const int NoMoreFiles = 18;
        private const int MaximumAncestryDepth = 64;
        private const uint UnknownParent = uint.MaxValue;

        private readonly object cacheGate = new object();
        private readonly Dictionary<ProcessPair, CacheEntry> cache = new Dictionary<ProcessPair, CacheEntry>();
        private readonly LinkedList<ProcessPair> recency = new LinkedList<ProcessPair>();
        private readonly Func<uint, long?> getStartTime;
        private readonly Func<uint, int, long?, bool?> classify;

        internal PlaceholderRequestProcessTree(ITracer tracer)
            : this(GetStartTime, (processId, ancestorId, startTime) => IsDescendant(processId, ancestorId, startTime, tracer))
        {
        }

        internal PlaceholderRequestProcessTree(Func<uint, long?> getStartTime, Func<uint, int, long?, bool?> classify)
        {
            this.getStartTime = getStartTime;
            this.classify = classify;
        }

        internal bool? IsDescendant(uint processId, NamedPipeMessages.LockData holder, long? ownerStartTime)
        {
            int ownerId = holder.PID;
            if (processId == ownerId)
            {
                return true;
            }

            if (processId == 0 || ownerId <= 0 || !ownerStartTime.HasValue)
            {
                return null;
            }

            long? requestStart = this.getStartTime(processId);
            if (!requestStart.HasValue || this.getStartTime((uint)ownerId) != ownerStartTime)
            {
                return null;
            }

            ProcessPair key = new ProcessPair(holder, ownerId, ownerStartTime.Value, processId, requestStart.Value);
            CacheEntry entry;
            lock (this.cacheGate)
            {
                if (this.cache.TryGetValue(key, out entry))
                {
                    this.recency.Remove(entry.Node);
                    this.recency.AddLast(entry.Node);
                }
                else
                {
                    if (this.cache.Count == MaximumCachedProcesses)
                    {
                        this.cache.Remove(this.recency.First.Value);
                        this.recency.RemoveFirst();
                    }

                    entry = new CacheEntry(
                        this.recency.AddLast(key),
                        () => this.classify(processId, ownerId, ownerStartTime));
                    this.cache.Add(key, entry);
                }
            }

            bool? related = null;
            try
            {
                related = entry.Result.Value;
                if (this.getStartTime(processId) != requestStart || this.getStartTime((uint)ownerId) != ownerStartTime)
                {
                    related = null;
                }

                return related;
            }
            finally
            {
                if (!related.HasValue)
                {
                    lock (this.cacheGate)
                    {
                        if (this.cache.TryGetValue(key, out CacheEntry current) && ReferenceEquals(current, entry))
                        {
                            this.cache.Remove(key);
                            this.recency.Remove(entry.Node);
                        }
                    }
                }
            }
        }

        internal static bool? IsDescendant(uint processId, int ancestorId, long? expectedAncestorStart, ITracer tracer)
        {
            if (processId == ancestorId)
            {
                return true;
            }

            if (processId == 0 || ancestorId <= 0 || !expectedAncestorStart.HasValue)
            {
                return null;
            }

            long? ancestorStart = GetStartTime((uint)ancestorId);
            long? requestStart = GetStartTime(processId);
            if (ancestorStart != expectedAncestorStart || !requestStart.HasValue)
            {
                return null;
            }

            using (SafeFileHandle snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0))
            {
                ProcessEntry entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
                if (snapshot.IsInvalid || !Process32First(snapshot, ref entry))
                {
                    TraceFailure(tracer, Marshal.GetLastWin32Error());
                    return null;
                }

                Dictionary<uint, uint> parents = new Dictionary<uint, uint>();
                do
                {
                    parents[entry.ProcessId] = entry.ParentProcessId;
                }
                while (Process32Next(snapshot, ref entry));

                int error = Marshal.GetLastWin32Error();
                if (error != NoMoreFiles)
                {
                    TraceFailure(tracer, error);
                    return null;
                }

                bool? related = IsDescendant(parents, processId, (uint)ancestorId, GetStartTime);
                if (GetStartTime(processId) != requestStart || GetStartTime((uint)ancestorId) != expectedAncestorStart)
                {
                    return null;
                }

                return related;
            }
        }

        internal static bool? IsDescendant(
            Dictionary<uint, uint> parents, uint processId, uint ancestorId, Func<uint, long?> getStartTime = null)
        {
            if (processId == 0 || ancestorId == 0 || !parents.ContainsKey(processId) || !parents.ContainsKey(ancestorId))
            {
                return null;
            }

            if (getStartTime != null)
            {
                Dictionary<uint, long?> starts = new Dictionary<uint, long?>();
                ValidateChain(parents, processId, starts, getStartTime);
                ValidateChain(parents, ancestorId, starts, getStartTime);
            }

            HashSet<uint> ownerAncestors = new HashSet<uint>();
            uint owner = ancestorId;
            HashSet<uint> ownerVisited = new HashSet<uint> { ancestorId };
            while (parents.TryGetValue(owner, out uint parent) && parent != 0 && parents.ContainsKey(parent))
            {
                if (!ownerVisited.Add(parent) || ownerVisited.Count > MaximumAncestryDepth)
                {
                    return null;
                }

                ownerAncestors.Add(parent);
                owner = parent;
            }

            HashSet<uint> visited = new HashSet<uint>();
            while (processId != 0 && visited.Count < MaximumAncestryDepth)
            {
                if (processId == ancestorId)
                {
                    return true;
                }

                if (ownerAncestors.Contains(processId))
                {
                    return false;
                }

                if (!visited.Add(processId) || !parents.TryGetValue(processId, out uint parentId))
                {
                    return null;
                }

                processId = parentId;
            }

            return processId == 0 ? false : null;
        }

        private static void ValidateChain(
            Dictionary<uint, uint> parents, uint processId, Dictionary<uint, long?> starts, Func<uint, long?> getStartTime)
        {
            HashSet<uint> visited = new HashSet<uint>();
            while (processId != 0 && visited.Count < MaximumAncestryDepth &&
                visited.Add(processId) && parents.TryGetValue(processId, out uint parentId) && parentId != 0)
            {
                if (!starts.TryGetValue(processId, out long? childStart))
                {
                    starts[processId] = childStart = getStartTime(processId);
                }

                if (!starts.TryGetValue(parentId, out long? parentStart))
                {
                    starts[parentId] = parentStart = getStartTime(parentId);
                }

                if (!childStart.HasValue || !parentStart.HasValue || parentStart > childStart)
                {
                    // A reused parent PID does not establish ancestry.
                    parents[processId] = UnknownParent;
                    return;
                }

                processId = parentId;
            }
        }

        private static long? GetStartTime(uint processId)
        {
            return processId <= int.MaxValue &&
                WindowsPlatform.TryGetActiveProcessStartTimeImplementation((int)processId, out long start) == ProcessStartTimeResult.Success
                ? start : null;
        }

        private static void TraceFailure(ITracer tracer, int error)
        {
            tracer.RelatedWarning(
                new EventMetadata { { "NativeErrorCode", error } },
                "Cannot determine placeholder request ancestry; retaining the Git-lock rejection policy");
        }

        private readonly record struct ProcessPair(
            NamedPipeMessages.LockData Holder, int OwnerId, long OwnerStart, uint RequestId, long RequestStart);

        private sealed class CacheEntry
        {
            public CacheEntry(LinkedListNode<ProcessPair> node, Func<bool?> classify)
            {
                this.Node = node;
                this.Result = new Lazy<bool?>(classify, LazyThreadSafetyMode.ExecutionAndPublication);
            }

            public LinkedListNode<ProcessPair> Node { get; }
            public Lazy<bool?> Result { get; }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);

        [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Process32First(SafeFileHandle snapshot, ref ProcessEntry entry);

        [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Process32Next(SafeFileHandle snapshot, ref ProcessEntry entry);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ProcessEntry
        {
            public uint Size;
            public uint Usage;
            public uint ProcessId;
            public UIntPtr DefaultHeap;
            public uint ModuleId;
            public uint ThreadCount;
            public uint ParentProcessId;
            public int Priority;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string ExecutableName;
        }
    }
}
