using GVFS.Common.Tracing;
using System;

namespace GVFS.Virtualization.Projection
{
    public partial class GitIndexProjection
    {
        /// <summary>
        /// This class is used to keep an array of objects that can be used.
        /// The size of the array is dynamically increased as objects get used.
        /// The size can be shrunk to eliminate having too many object allocated
        /// This class is not thread safe and is intended to only be used when parsing the git index
        /// which is currently single threaded.
        /// </summary>
        /// <typeparam name="T">The type of object to be stored in the array pool</typeparam>
        internal class ObjectPool<T>
        {
            private const int MinPoolSize = 100;
            private int allocationSize;
            private T[] pool;
            private int freeIndex;
            private Func<T> objectCreator;
            private ITracer tracer;

            public ObjectPool(ITracer tracer, int allocationSize, Func<T> objectCreator)
            {
                if (allocationSize < MinPoolSize)
                {
                    allocationSize = MinPoolSize;
                }

                this.tracer = tracer;
                this.objectCreator = objectCreator;
                this.allocationSize = allocationSize;
                this.pool = new T[0];
                this.ResizePool(allocationSize);
                this.AllocateObjects(startIndex: 0);
            }

            public int ObjectsUsed
            {
                get { return this.freeIndex; }
            }

            public int Size
            {
                get { return this.pool.Length; }
            }

            public int FreeIndex
            {
                get { return this.freeIndex; }
                protected set { this.freeIndex = value; }
            }

            protected T[] Pool
            {
                get { return this.pool; }
            }

            public T GetNew()
            {
                this.EnsureRoomInPool();
                return this.pool[this.freeIndex++];
            }

            public void FreeAll()
            {
                this.freeIndex = 0;
            }

            public void Shrink()
            {
                using (ITracer tracer = this.tracer.StartActivity("ShrinkPool", EventLevel.Informational))
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Area", EtwArea);
                    metadata.Add("PoolType", typeof(T).Name);
                    metadata.Add("CurrentSize", this.pool.Length);

                    bool didShrink = false;

                    // Keep extra objects so we don't have to expand on the very next GetNew() call
                    // and make sure that the shrink will reclaim at least a percentage of the objects
                    int shrinkToSize = Convert.ToInt32(this.freeIndex * PoolAllocationMultipliers.ShrinkExtraObjects);
                    if (this.pool.Length * PoolAllocationMultipliers.ShrinkMinPoolSize > shrinkToSize)
                    {
                        this.ResizePool(shrinkToSize);
                        didShrink = true;
                    }

                    metadata.Add(nameof(didShrink), didShrink);
                    metadata.Add(nameof(shrinkToSize), shrinkToSize);

                    tracer.Stop(metadata);
                }
            }

            public virtual void UnpinPool()
            {
            }

            protected void ExpandPool()
            {
                using (ITracer tracer = this.tracer.StartActivity("ExpandPool", EventLevel.Informational))
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Area", EtwArea);
                    metadata.Add("PoolType", typeof(T).Name);

                    int previousSize = this.pool.Length;

                    // The values for shrinking and expanding are currently set to prevent the pool from shrinking after expanding
                    //
                    // Example using
                    // ExpandPoolNewObjects = 0.15
                    // ShrinkExtraObjects = 1.1
                    // ShrinkMinPoolSize = 0.9
                    //
                    // Pool at 1000 will get expanded to 1150 and if only one new object is used the free index will be 1001
                    //
                    // The shrink code will check
                    // 1001 * 1.1 = 1101 - shrinkToSize
                    // 1150 * 0.9 = 1035 - shrink threshold
                    // 1035 > 1101 - do not shrink the pool
                    int newObjects = Convert.ToInt32(previousSize * PoolAllocationMultipliers.ExpandPoolNewObjects);

                    // If the previous size of the pool was a lot smaller than what was first allocated and
                    // set as the allocation size, just expand back up to the originally set allocation size
                    if (previousSize * (1 + PoolAllocationMultipliers.ExpandPoolNewObjects) < this.allocationSize)
                    {
                        newObjects = this.allocationSize - previousSize;
                    }

                    this.ResizePool(previousSize + newObjects);

                    this.AllocateObjects(previousSize);

                    metadata.Add("PreviousSize", previousSize);
                    metadata.Add("NewSize", this.pool.Length);
                    tracer.Stop(metadata);
                }
            }

            protected virtual void PinPool()
            {
            }

            private void EnsureRoomInPool()
            {
                if (this.freeIndex >= this.pool.Length)
                {
                    this.ExpandPool();
                }
            }

            private void ResizePool(int newSize)
            {
                this.UnpinPool();
                Array.Resize(ref this.pool, newSize);
                this.PinPool();
            }

            private void AllocateObjects(int startIndex)
            {
                if (this.objectCreator != null)
                {
                    for (int i = startIndex; i < this.pool.Length; i++)
                    {
                        this.pool[i] = this.objectCreator();
                    }
                }
            }
        }
    }
}