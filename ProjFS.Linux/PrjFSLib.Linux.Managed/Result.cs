using Mono.Unix.Native;

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
        EDirectoryNotEmpty                  = 0x20000200,
        EVirtualizationInvalidOperation     = 0x20000400,

        ENotYetImplemented                  = 0xFFFFFFFF,
    }

    internal static class ResultMethods
    {
        internal static int ConvertResultToErrno(this Result result)
        {
            switch (result)
            {
                case Result.Success:
                    return 0;
                case Result.Pending:
                    return -(int)Errno.EINPROGRESS;

                case Result.EInvalidArgs:
                    return -(int)Errno.EINVAL;
                case Result.EInvalidOperation:
                case Result.EVirtualizationInvalidOperation:
                    return -(int)Errno.EPERM;
                case Result.ENotSupported:
                    return -(int)Errno.EOPNOTSUPP;

                case Result.EDriverNotLoaded:
                    return -(int)Errno.ENODEV;
                case Result.EOutOfMemory:
                    return -(int)Errno.ENOMEM;
                case Result.EFileNotFound:
                case Result.EPathNotFound:
                    return -(int)Errno.ENOENT;
                case Result.EAccessDenied:
                    return -(int)Errno.EPERM;
                case Result.EInvalidHandle:
                    return -(int)Errno.EBADF;
                case Result.EIOError:
                    return -(int)Errno.EIO;
                case Result.EDirectoryNotEmpty:
                    return -(int)Errno.ENOTEMPTY;

                case Result.ENotYetImplemented:
                    return -(int)Errno.ENOSYS;

                case Result.Invalid:
                default:
                    return -(int)Errno.EINVAL;
            }
        }

        internal static Result ConvertErrnoToResult(this Errno errno)
        {
            switch (errno)
            {
                case 0:
                    return Result.Success;

                case Errno.EACCES:
                case Errno.EEXIST:
                case Errno.EPERM:
                case Errno.EROFS:
                    return Result.EAccessDenied;
                case Errno.EBADF:
                    return Result.EInvalidHandle;
                case Errno.EDQUOT:
                case Errno.EIO:
                case Errno.ENODATA: // also ENOATTR; see getxattr(2)
                case Errno.ENOSPC:
                    return Result.EIOError;
                case Errno.EFAULT:
                case Errno.EINVAL:
                case Errno.EOVERFLOW:
                    return Result.EInvalidArgs;
                case Errno.ELOOP:
                case Errno.EMLINK:
                case Errno.ENAMETOOLONG:
                case Errno.ENOENT:
                case Errno.ENOTDIR:
                    return Result.EPathNotFound;
                case Errno.ENOMEM:
                    return Result.EOutOfMemory;
                case Errno.ENOSYS:
                    return Result.ENotYetImplemented;
                case Errno.ENOTEMPTY:
                    return Result.EDirectoryNotEmpty;
                case Errno.EOPNOTSUPP:
                    return Result.ENotSupported;

                default:
                    return Result.Invalid;
            }
        }
    }
}