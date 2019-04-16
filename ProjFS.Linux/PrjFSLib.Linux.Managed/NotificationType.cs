using System;

namespace PrjFSLib.Linux
{
    [Flags]
    public enum NotificationType
    {
        Invalid             = 0x00000000,

        None                = 0x00000001,
        NewFileCreated      = 0x00000004,
        PreDelete           = 0x00000010,
        FileRenamed         = 0x00000080,
        HardLinkCreated     = 0x00000100,
        PreConvertToFull    = 0x00001000,

        FileModified        = 0x10000002,
    }
}
