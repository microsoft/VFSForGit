using GVFS.Tests.Should;
using GVFS.Virtualization.Projection;
using NUnit.Framework;

namespace GVFS.UnitTests.Virtualization
{
    [TestFixture]
    public class ObjectPoolTests
    {
        private const int DefaultObjectsToUse = 23;
        private const int DefaultShrinkSize = DefaultObjectsToUse + (DefaultObjectsToUse / 10);
        private const int PoolSizeAfterUsingDefault = (2 * AllocationSize) + AllocationSize;
        private const int PoolSizeAfterExpandingAfterShrinking = DefaultShrinkSize + (AllocationSize * 3);
        private const int AllocationSize = 10;

        [TestCase]
        public void TestGettingObjects()
        {
            CreateExpandedPool();
        }

        [TestCase]
        public void ShrinkKeepsPoolSizePlusPercent()
        {
            GitIndexProjection.ObjectPool<object> pool = CreateExpandedPool();
            pool.Shrink();
            pool.Size.ShouldEqual(DefaultShrinkSize);
            UseObjectsInPool(pool, DefaultObjectsToUse);
            pool.Size.ShouldEqual(PoolSizeAfterExpandingAfterShrinking);
            pool.FreeAll();
            UseObjectsInPool(pool, DefaultObjectsToUse);
            pool.Size.ShouldEqual(PoolSizeAfterExpandingAfterShrinking);
            pool.Shrink();
            pool.Size.ShouldEqual(DefaultShrinkSize);
        }

        [TestCase]
        public void FreeToZeroAllocatesMinimumSizeNextGet()
        {
            GitIndexProjection.ObjectPool<object> pool = new GitIndexProjection.ObjectPool<object>(AllocationSize, objectCreator: () => new object());
            pool.FreeAll();
            pool.Shrink();
            pool.Size.ShouldEqual(0);
            UseObjectsInPool(pool, 1);
            pool.Size.ShouldEqual(AllocationSize);
        }

        [TestCase]
        public void FreeKeepsPoolSize()
        {
            GitIndexProjection.ObjectPool<object> pool = CreateExpandedPool();
            pool.Shrink();
            pool.Size.ShouldEqual(DefaultShrinkSize);
            pool.FreeAll();
            pool.Size.ShouldEqual(DefaultShrinkSize);
            UseObjectsInPool(pool, DefaultObjectsToUse);
            UseObjectsInPool(pool, DefaultObjectsToUse);
            pool.Size.ShouldEqual(PoolSizeAfterExpandingAfterShrinking);
            pool.FreeAll();
            pool.Size.ShouldEqual(PoolSizeAfterExpandingAfterShrinking);
        }

        private static GitIndexProjection.ObjectPool<object> CreateExpandedPool()
        {
            GitIndexProjection.ObjectPool<object> pool = new GitIndexProjection.ObjectPool<object>(AllocationSize, objectCreator: () => new object());
            UseObjectsInPool(pool, DefaultObjectsToUse);
            pool.Size.ShouldEqual(PoolSizeAfterUsingDefault);
            return pool;
        }

        private static void UseObjectsInPool(GitIndexProjection.ObjectPool<object> pool, int count)
        {
            for (int i = 0; i < count; i++)
            {
                pool.GetNew().ShouldNotBeNull();
            }
        }
    }
}
