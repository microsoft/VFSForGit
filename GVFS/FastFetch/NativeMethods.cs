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
            if (!isUnixOS)
            {
                NativeWindowsMethods.WriteFile(tracer, originalData, originalSize, destination, mode);
            }
            else
            {
                NativeUnixMethods.WriteFile(tracer, originalData, originalSize, destination, mode);
            }
        }

        public static bool StatAndUpdateIndexForFile(string path, MemoryMappedViewAccessor indexView, long offset)
        {
            if (!isUnixOS)
            {
                return NativeWindowsMethods.StatAndUpdateIndexForFile(path, indexView, offset);
            }
            else
            {
                return NativeUnixMethods.StatAndUpdateIndexForFile(path, indexView, offset);
            }
        }
    }
}
