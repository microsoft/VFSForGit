using System;
using System.Runtime.InteropServices;

namespace PrjFSLib.Mac.Interop
{
    internal static class PrjFSLib
    {
        public const int PlaceholderIdLength = 128;
        private const string PrjFSLibPath = "libPrjFSLib.dylib";

        [DllImport(PrjFSLibPath, EntryPoint = "PrjFS_StartVirtualizationInstance")]
        public static extern Result StartVirtualizationInstance(
            string virtualizationRootFullPath,
            Callbacks callbacks,
            uint poolThreadCount);

        [DllImport(PrjFSLibPath, EntryPoint = "PrjFS_ConvertDirectoryToVirtualizationRoot")]
        public static extern Result ConvertDirectoryToVirtualizationRoot(
            string virtualizationRootFullPath);

        [DllImport(PrjFSLibPath, EntryPoint = "PrjFS_WritePlaceholderDirectory")]
        public static extern Result WritePlaceholderDirectory(
            string relativePath);

        [DllImport(PrjFSLibPath, EntryPoint = "PrjFS_WritePlaceholderFile")]
        public static extern Result WritePlaceholderFile(
            string relativePath,
        
            [MarshalAs(UnmanagedType.LPArray, SizeConst = PlaceholderIdLength)]
            byte[] providerId,

            [MarshalAs(UnmanagedType.LPArray, SizeConst = PlaceholderIdLength)]
            byte[] contentId,
        
            ulong fileSize,
            UInt16 fileMode);

        [DllImport(PrjFSLibPath, EntryPoint = "PrjFS_WriteFileContents")]
        public static extern Result WriteFileContents(
            IntPtr fileHandle,
            IntPtr bytes,
            uint byteCount);
    }
}
