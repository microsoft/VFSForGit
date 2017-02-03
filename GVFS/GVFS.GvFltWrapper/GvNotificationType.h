#pragma once

namespace GVFSGvFltWrapper
{
    [System::FlagsAttribute]
    public enum class GvNotificationType : unsigned long
    {
        NotificationNone = GV_NOTIFICATION_NONE,
        NotificationPostCreate = GV_NOTIFICATION_POST_CREATE,
        NotificationPreDelete = GV_NOTIFICATION_PRE_DELETE,
        NotificationPreRename = GV_NOTIFICATION_PRE_RENAME,
        NotificationPreSetHardlink = GV_NOTIFICATION_PRE_SET_HARDLINK,
        NotificationFileRenamed = GV_NOTIFICATION_FILE_RENAMED,
        NotificationHardlinkCreated = GV_NOTIFICATION_HARDLINK_CREATED,
        NotificationFileHandleClosed = GV_NOTIFICATION_FILE_HANDLE_CLOSED,
    };
}
