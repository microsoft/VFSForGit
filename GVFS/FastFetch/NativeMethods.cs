using GVFS.Common.Tracing;
using System.IO.MemoryMappedFiles;

namespace FastFetch
{
    internal static class NativeMethods
    {
        public static unsafe void WriteFile(ITracer tracer, byte* originalData, long originalSize, string destination, ushort mode)
        {
            NativeWindowsMethods.WriteFile(tracer, originalData, originalSize, destination);
        }

        public static bool TryStatFileAndUpdateIndex(ITracer tracer, string path, MemoryMappedViewAccessor indexView, long offset)
        {
            return NativeWindowsMethods.TryStatFileAndUpdateIndex(tracer, path, indexView, offset);
        }
    }
}
