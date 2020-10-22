using GVFS.Common.Tracing;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace FastFetch
{
    internal static class NativeMethods
    {
        public static bool isUnixOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

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
