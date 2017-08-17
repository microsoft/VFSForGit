#pragma once

namespace GvFlt
{
    [System::FlagsAttribute]
    public enum class NotificationType : unsigned long
    {
        None = GV_NOTIFICATION_NONE,
        PostCreate = GV_NOTIFICATION_POST_CREATE,
        PreDelete = GV_NOTIFICATION_PRE_DELETE,
        PreRename = GV_NOTIFICATION_PRE_RENAME,
        PreSetHardlink = GV_NOTIFICATION_PRE_SET_HARDLINK,
        FileRenamed = GV_NOTIFICATION_FILE_RENAMED,
        HardlinkCreated = GV_NOTIFICATION_HARDLINK_CREATED,
        FileHandleClosed = GV_NOTIFICATION_FILE_HANDLE_CLOSED,
    };
}
