namespace PrjFSLib.Linux
{
    public enum Result : uint
    {
        Invalid                             = 0x00000000,

        Success                             = 0x00000001,
        Pending                             = 0x00000002,

        // Bugs in the caller
        EInvalidArgs                        = 0x10000001,
        EInvalidOperation                   = 0x10000002,
        ENotSupported                       = 0x10000004,

        // Runtime errors
        EDriverNotLoaded                    = 0x20000001,
        EOutOfMemory                        = 0x20000002,
        EFileNotFound                       = 0x20000004,
        EPathNotFound                       = 0x20000008,
        EAccessDenied                       = 0x20000010,
        EInvalidHandle                      = 0x20000020,
        EIOError                            = 0x20000040,
        ENotAVirtualizationRoot             = 0x20000080,
        EVirtualizationRootAlreadyExists    = 0x20000100,
        EDirectoryNotEmpty                  = 0x20000200,

        ENotYetImplemented                  = 0xFFFFFFFF,
    }
}
