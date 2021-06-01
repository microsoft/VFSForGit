using GVFS.Common.Tracing;
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace GVFS.Virtualization.Projection
{
    public partial class GitIndexProjection
    {
        internal class LazyUTF8String
        {
            private static ObjectPool<LazyUTF8String> stringPool;
            private static BytePool bytePool;

            private int startIndex;
            private int length;

            private string utf16string;

            public LazyUTF8String()
            {
            }

            public LazyUTF8String(string value)
            {
                this.utf16string = value;
                this.startIndex = -1;
                this.length = -1;
            }

            public static void InitializePools(ITracer tracer, uint indexEntryCount)
            {
                if (stringPool == null)
                {
                    stringPool = new ObjectPool<LazyUTF8String>(tracer, Convert.ToInt32(indexEntryCount * PoolAllocationMultipliers.StringPool), objectCreator: () => new LazyUTF8String());
                }

                if (bytePool == null)
                {
                    bytePool = new BytePool(tracer, indexEntryCount);
                }
            }

            public static void ResetPool(ITracer tracer, uint indexEntryCount)
            {
                stringPool = new ObjectPool<LazyUTF8String>(tracer, Convert.ToInt32(indexEntryCount * PoolAllocationMultipliers.StringPool), objectCreator: () => new LazyUTF8String());
                if (bytePool != null)
                {
                    bytePool.UnpinPool();
                }

                bytePool = new BytePool(tracer, indexEntryCount);
            }

            public static void FreePool()
            {
                if (bytePool != null)
                {
                    bytePool.FreeAll();
                }

                if (stringPool != null)
                {
                    stringPool.FreeAll();
                }
            }

            public static int StringPoolSize()
            {
                return stringPool.Size;
            }

            public static int BytePoolSize()
            {
                return bytePool.Size;
            }

            public static void ShrinkPool()
            {
                bytePool.Shrink();
                stringPool.Shrink();
            }

            public static unsafe LazyUTF8String FromByteArray(byte* bufferPtr, int length)
            {
                bytePool.MakeFreeSpace(length);
                LazyUTF8String lazyString = stringPool.GetNew();

                byte* poolPtrForLoop = bytePool.RawPointer + bytePool.FreeIndex;
                byte* bufferPtrForLoop = bufferPtr;
                int index = 0;

                while (index < length)
                {
                    if (*bufferPtrForLoop <= 127)
                    {
                        *poolPtrForLoop = *bufferPtrForLoop;
                    }
                    else
                    {
                        // The string has non-ASCII characters in it, fall back to full parsing
                        lazyString.SetToString(Encoding.UTF8.GetString(bufferPtr, length));
                        return lazyString;
                    }

                    ++poolPtrForLoop;
                    ++bufferPtrForLoop;
                    ++index;
                }

                lazyString.ResetState(bytePool.FreeIndex, length);
                bytePool.AdvanceFreeIndex(length);
                return lazyString;
            }

            public unsafe int Compare(LazyUTF8String other, bool caseSensitive)
            {
                // If we've already converted to a .NET String, use their implementation because it's likely to contain
                // extended characters, which we're not set up to handle below
                if (this.utf16string != null ||
                    other.utf16string != null)
                {
                    return string.Compare(this.GetString(), other.GetString(), caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
                }

                // We now know that both strings are ASCII, because if they had extended characters they would
                // have already been created as string objects

                int minLength = this.length <= other.length ? this.length : other.length;

                byte* thisPtr = bytePool.RawPointer + this.startIndex;
                byte* otherPtr = bytePool.RawPointer + other.startIndex;
                int count = 0;

                // Case-sensitive comparison; always returns and never proceeds to case-insensitive comparison
                if (caseSensitive)
                {
                    while (count < minLength)
                    {
                        if (*thisPtr != *otherPtr)
                        {
                            byte thisC = *thisPtr;
                            byte otherC = *otherPtr;
                            return thisC - otherC;
                        }

                        ++thisPtr;
                        ++otherPtr;
                        ++count;
                    }

                    return this.length - other.length;
                }

                // Case-insensitive comparison
                while (count < minLength)
                {
                    if (*thisPtr != *otherPtr)
                    {
                        byte thisC = *thisPtr;
                        byte otherC = *otherPtr;

                        // The more intuitive approach to checking IsLower() is to do two comparisons to see if c is within the range 'a'-'z'.
                        // However since byte is unsigned, we can rely on underflow to satisfy both conditions with one comparison.
                        //      if c < 'a', (c - 'a') will underflow and become a large positive number, hence > ('z' - 'a')
                        //      if c > 'z', (c - 'a') will naturally be > ('z' - 'a')
                        //      else the condition is satisfied and we know it is lower-case

                        // Note: We only want to do the ToUpper calculation if one char is lower-case and the other char is not.
                        // If they are both lower-case, they can be safely compared as is.

                        //// if (thisC.IsLower())
                        if ((byte)(thisC - 'a') <= 'z' - 'a')
                        {
                            //// if (!otherC.IsLower())
                            if ((byte)(otherC - 'a') > 'z' - 'a')
                            {
                                //// thisC = thisC.ToUpper();
                                thisC -= 'a' - 'A';
                            }
                        }
                        else
                        {
                            //// else, we know !thisC.IsLower()

                            //// if (otherC.IsLower())
                            if ((byte)(otherC - 'a') <= 'z' - 'a')
                            {
                                //// otherC = otherC.ToUpper();
                                otherC -= 'a' - 'A';
                            }
                        }

                        if (thisC != otherC)
                        {
                            return thisC - otherC;
                        }
                    }

                    ++thisPtr;
                    ++otherPtr;
                    ++count;
                }

                return this.length - other.length;
            }

            public unsafe int CaseInsensitiveCompare(LazyUTF8String other)
            {
                return this.Compare(other, caseSensitive: false);
            }

            public unsafe int CaseSensitiveCompare(LazyUTF8String other)
            {
                return this.Compare(other, caseSensitive: true);
            }

            public bool CaseInsensitiveEquals(LazyUTF8String other)
            {
                return this.CaseInsensitiveCompare(other) == 0;
            }

            public bool CaseSensitiveEquals(LazyUTF8String other)
            {
                return this.CaseSensitiveCompare(other) == 0;
            }

            public unsafe string GetString()
            {
                if (this.utf16string == null)
                {
                    // Confirmed earlier that the bytes are all ASCII
                    this.utf16string = Encoding.ASCII.GetString(bytePool.RawPointer + this.startIndex, this.length);
                }

                return this.utf16string;
            }

            private void SetToString(string value)
            {
                this.utf16string = value;

                this.startIndex = -1;
                this.length = -1;
            }

            private void ResetState(int startIndex, int length)
            {
                this.startIndex = startIndex;
                this.length = length;

                this.utf16string = null;
            }

            private class BytePool : ObjectPool<byte>
            {
                private GCHandle poolHandle;

                public BytePool(ITracer tracer, uint indexEntryCount)
                    : base(tracer, Convert.ToInt32(indexEntryCount * PoolAllocationMultipliers.BytePool), null)
                {
                }

                public unsafe byte* RawPointer { get; private set; }

                public void MakeFreeSpace(int count)
                {
                    if (this.FreeIndex + count > this.Pool.Length)
                    {
                        this.ExpandPool();
                    }
                }

                public void AdvanceFreeIndex(int length)
                {
                    this.FreeIndex += length;
                }

                public override unsafe void UnpinPool()
                {
                    if (this.poolHandle.IsAllocated)
                    {
                        this.poolHandle.Free();
                        this.RawPointer = (byte*)IntPtr.Zero.ToPointer();
                    }
                }

                protected override unsafe void PinPool()
                {
                    this.poolHandle = GCHandle.Alloc(this.Pool, GCHandleType.Pinned);
                    this.RawPointer = (byte*)this.poolHandle.AddrOfPinnedObject().ToPointer();
                }
            }
        }
    }
}
