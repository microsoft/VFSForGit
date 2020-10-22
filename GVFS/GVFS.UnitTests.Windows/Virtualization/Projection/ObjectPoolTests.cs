using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using GVFS.Virtualization.Projection;
using NUnit.Framework;
using System;

namespace GVFS.UnitTests.Virtualization
{
    [TestFixture]
    public class ObjectPoolTests
    {
        private const int DefaultObjectsToUse = 101;
        private const int AllocationSize = 100;
        private static readonly int PoolSizeAfterUsingDefault = Convert.ToInt32(AllocationSize * 1.15);
        private static readonly int DefaultShrinkSize = PoolSizeAfterUsingDefault;
        private static readonly int PoolSizeAfterExpandingAfterShrinking = Convert.ToInt32(AllocationSize * 1.15 * 1.15);

        [TestCase]
        public void TestGettingObjects()
        {
            CreateExpandedPool();
        }

        [TestCase]
        public void ShrinkKeepsUsedObjectsPlusPercent()
        {
            GitIndexProjection.ObjectPool<object> pool = new GitIndexProjection.ObjectPool<object>(new MockTracer(), AllocationSize, objectCreator: () => new object());
            UseObjectsInPool(pool, 20);
            pool.Size.ShouldEqual(AllocationSize);
            pool.Shrink();
            pool.Size.ShouldEqual(Convert.ToInt32(20 * 1.1));
        }

        [TestCase]
        public void FreeToZeroAllocatesMinimumSizeNextGet()
        {
            GitIndexProjection.ObjectPool<object> pool = new GitIndexProjection.ObjectPool<object>(new MockTracer(), AllocationSize, objectCreator: () => new object());
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
            pool.FreeAll();
            pool.Size.ShouldEqual(DefaultShrinkSize);
            UseObjectsInPool(pool, DefaultObjectsToUse);
            UseObjectsInPool(pool, 15);
            pool.Size.ShouldEqual(PoolSizeAfterExpandingAfterShrinking);
            pool.FreeAll();
            pool.Size.ShouldEqual(PoolSizeAfterExpandingAfterShrinking);
        }

        private static GitIndexProjection.ObjectPool<object> CreateExpandedPool()
        {
            GitIndexProjection.ObjectPool<object> pool = new GitIndexProjection.ObjectPool<object>(new MockTracer(), AllocationSize, objectCreator: () => new object());
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
