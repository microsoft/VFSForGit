#pragma once

namespace GvLib
{
    /// <summary>
    /// Types of notifications that for which a provider can register
    /// </summary>
    [System::FlagsAttribute]
    public enum class NotificationType : ULONG
    {
        /// <summary> Register for no notification callbacks </summary>
        None = GV_NOTIFICATION_NONE,

        /// <summary> Register for OnNotifyFileOpened callback </summary>
        FileOpened = GV_NOTIFICATION_FILE_OPENED,

        /// <summary> Register for OnNotifyNewFileCreated callback </summary>
        NewFileCreated = GV_NOTIFICATION_NEW_FILE_CREATED,

        /// <summary> Register for OnNotifyFileSupersededOrOverwritten callback </summary>
        FileSupersededOrOverwritten = GV_NOTIFICATION_FILE_SUPERSEDED_OR_OVERWRITTEN,

        /// <summary> Register for OnNotifyPreDelete callback </summary>
        PreDelete = GV_NOTIFICATION_PRE_DELETE,

        /// <summary> Register for OnNotifyPreRename callback </summary>
        PreRename = GV_NOTIFICATION_PRE_RENAME,

        /// <summary> Register for OnNotifyPreSetHardlink callback </summary>
        PreSetHardlink = GV_NOTIFICATION_PRE_SET_HARDLINK,

        /// <summary> Register for OnNotifyFileRenamed callback </summary>
        FileRenamed = GV_NOTIFICATION_FILE_RENAMED,

        /// <summary> Register for OnNotifyHardlinkCreated callback </summary>
        HardlinkCreated = GV_NOTIFICATION_HARDLINK_CREATED,

        /// <summary> Register for OnNotifyFileHandleClosedNoModification callback </summary>
        FileHandleClosedNoModification = GV_NOTIFICATION_FILE_HANDLE_CLOSED_NO_MODIFICATION,

        /// <summary> Register to receive OnNotifyFileHandleClosedFileModifiedOrDeleted callback when a file is modified</summary>
        FileHandleClosedFileModified = GV_NOTIFICATION_FILE_HANDLE_CLOSED_FILE_MODIFIED,

        /// <summary> Register to receive OnNotifyFileHandleClosedModifiedOrDeleted callback when a file is deleted </summary>
        FileHandleClosedFileDeleted = GV_NOTIFICATION_FILE_HANDLE_CLOSED_FILE_DELETED,

        /// <summary> 
        /// Continue to use the notifications specified in globalNotificationMask when 
        /// StartVirtualizationInstance was called
        /// </summary>
        /// <remarks>
        /// This value should not be passed to StartVirtualizationInstance, it is only allowed in the output
        /// parameter of those OnNotify callbacks that allow for registering for notifications. 
        /// This is the default value for the OnNotify callbacks that have a NotificationType output parameter.
        /// </remarks>
        UseGlobalMask = static_cast<std::underlying_type_t<NotificationType>>(GV_NOTIFICATION_USE_GLOBAL_MASK)
    };
}
