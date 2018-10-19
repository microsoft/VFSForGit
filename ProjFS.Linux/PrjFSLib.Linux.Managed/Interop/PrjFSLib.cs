using System;
using System.Runtime.InteropServices;

namespace PrjFSLib.Linux.Interop
{
    internal static class PrjFSLib
    {
        // TODO(Linux): set value from that defined in Linux library header
        public const int PlaceholderIdLength = 128;
        private const string PrjFSLibPath = "libprojfs.so";

        // TODO(Linux): revise library functions for Linux
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
            ushort fileMode);

        [DllImport(PrjFSLibPath, EntryPoint = "PrjFS_WriteSymLink")]
        public static extern Result WriteSymLink(
            string relativePath,
            string symLinkTarget);

        [DllImport(PrjFSLibPath, EntryPoint = "PrjFS_UpdatePlaceholderFileIfNeeded")]
        public static extern Result UpdatePlaceholderFileIfNeeded(
            string relativePath,
            [MarshalAs(UnmanagedType.LPArray, SizeConst = PlaceholderIdLength)]
            byte[] providerId,
            [MarshalAs(UnmanagedType.LPArray, SizeConst = PlaceholderIdLength)]
            byte[] contentId,
            ulong fileSize,
            ushort fileMode,
            UpdateType updateType,
            ref UpdateFailureCause failureCause);

        [DllImport(PrjFSLibPath, EntryPoint = "PrjFS_ReplacePlaceholderFileWithSymLink")]
        public static extern Result ReplacePlaceholderFileWithSymLink(
            string relativePath,
            string symLinkTarget,
            UpdateType updateType,
            ref UpdateFailureCause failureCause);

        [DllImport(PrjFSLibPath, EntryPoint = "PrjFS_DeleteFile")]
        public static extern Result DeleteFile(
            string relativePath,
            UpdateType updateType,
            ref UpdateFailureCause failureCause);

        [DllImport(PrjFSLibPath, EntryPoint = "PrjFS_WriteFileContents")]
        public static extern Result WriteFileContents(
            IntPtr fileHandle,
            IntPtr bytes,
            uint byteCount);
    }
}
