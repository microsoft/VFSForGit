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
            private int allocationSize;
            private T[] pool;
            private int freeIndex;
            private bool isWarmingUp = true;
            private Func<T> objectCreator;

            public ObjectPool(int allocationSize, Func<T> objectCreator)
            {
                if (allocationSize <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(allocationSize), "Must be greater than zero");
                }

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

            public bool Shrink()
            {
                bool didShrink = false;

                // Keep 10% extra objects so we don't have to expand on the very next GetNew() call
                // and make sure that the shrink will reclaim at least 10% of the objects
                int shrinkToSize = Convert.ToInt32(this.freeIndex * 1.1);
                if (this.pool.Length * 0.9 > shrinkToSize)
                {
                    this.ResizePool(shrinkToSize);
                    didShrink = true;
                }

                this.isWarmingUp = false;
                return didShrink;
            }

            public virtual void UnpinPool()
            {
            }

            protected void ExpandPool()
            {
                int previousSize = this.pool.Length;
                if (this.isWarmingUp)
                {
                    this.ResizePool((2 * previousSize) + this.allocationSize);
                }
                else
                {
                    int newObjects = Math.Max(this.allocationSize, Convert.ToInt32(previousSize * 0.1));
                    this.ResizePool(previousSize + newObjects);
                }

                this.AllocateObjects(previousSize);
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