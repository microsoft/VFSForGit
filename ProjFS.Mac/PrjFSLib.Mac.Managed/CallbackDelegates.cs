using System;
using System.Runtime.InteropServices;

namespace PrjFSLib.Mac
{
    // Callbacks
    public delegate Result EnumerateDirectoryCallback(
        ulong commandId,
        string relativePath,
        int triggeringProcessId,
        string triggeringProcessName);

    public delegate Result GetFileStreamCallback(
        ulong commandId,
        string relativePath,
        [MarshalAs(UnmanagedType.LPArray, SizeConst = Interop.PrjFSLib.PlaceholderIdLength)]
        byte[] providerId,
        [MarshalAs(UnmanagedType.LPArray, SizeConst = Interop.PrjFSLib.PlaceholderIdLength)]
        byte[] contentId,
        int triggeringProcessId,
        string triggeringProcessName,
        IntPtr fileHandle);

    public delegate Result NotifyOperationCallback(
        ulong commandId,
        string relativePath,
        string relativeFromPath,
        [MarshalAs(UnmanagedType.LPArray, SizeConst = Interop.PrjFSLib.PlaceholderIdLength)]
        byte[] providerId,
        [MarshalAs(UnmanagedType.LPArray, SizeConst = Interop.PrjFSLib.PlaceholderIdLength)]
        byte[] contentId,
        int triggeringProcessId,
        string triggeringProcessName,
        bool isDirectory,
        NotificationType notificationType);

    public delegate void LogErrorCallback(
        string errorMessage);

    public delegate void LogWarningCallback(
        string warningMessage);

    public delegate void LogInfoCallback(
        string infoMessage);

    // Pre-event notifications
    public delegate Result NotifyPreDeleteEvent(
        string relativePath,
        bool isDirectory);

    public delegate Result NotifyFilePreConvertToFullEvent(
        string relativePath);

    // Informational post-event notifications
    public delegate void NotifyNewFileCreatedEvent(
        string relativePath,
        bool isDirectory);

    public delegate void NotifyFileRenamedEvent(
        string relativeDestinationPath,
        bool isDirectory);

    public delegate void NotifyHardLinkCreatedEvent(
        string relativeSourcePath,
        string relativeNewLinkPath);

    public delegate void NotifyFileModified(
        string relativePath);
}
