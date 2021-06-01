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
            if (isUnixOS)
            {
                NativeUnixMethods.WriteFile(tracer, originalData, originalSize, destination, mode);
            }
            else
            {
                NativeWindowsMethods.WriteFile(tracer, originalData, originalSize, destination);
            }
        }

        public static bool TryStatFileAndUpdateIndex(ITracer tracer, string path, MemoryMappedViewAccessor indexView, long offset)
        {
            if (isUnixOS)
            {
                return NativeUnixMethods.TryStatFileAndUpdateIndex(tracer, path, indexView, offset);
            }
            else
            {
                return NativeWindowsMethods.TryStatFileAndUpdateIndex(tracer, path, indexView, offset);
            }
        }
    }
}
