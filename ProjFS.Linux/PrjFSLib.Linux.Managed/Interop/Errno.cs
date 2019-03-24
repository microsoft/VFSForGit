namespace PrjFSLib.Linux.Interop
{
    internal static partial class Errno
    {
        internal static Result ToResult(this int errno)
        {
            switch (errno)
            {
                case 0:
                    return Result.Success;

                case Constants.EACCES:
                case Constants.EEXIST:
                case Constants.EPERM:
                case Constants.EROFS:
                    return Result.EAccessDenied;
                case Constants.EBADF:
                    return Result.EInvalidHandle;
                case Constants.EDQUOT:
                case Constants.EIO:
                case Constants.ENODATA:  // equal to ENOATTR; see getxattr(2)
                case Constants.ENOSPC:
                    return Result.EIOError;
                case Constants.EFAULT:
                case Constants.EINVAL:
                case Constants.EOVERFLOW:
                    return Result.EInvalidArgs;
                case Constants.ELOOP:
                case Constants.EMLINK:
                case Constants.ENAMETOOLONG:
                case Constants.ENOENT:
                case Constants.ENOTDIR:
                    return Result.EPathNotFound;
                case Constants.ENOMEM:
                    return Result.EOutOfMemory;
                case Constants.ENOSYS:
                    return Result.ENotYetImplemented;
                case Constants.ENOTEMPTY:
                    return Result.EDirectoryNotEmpty;
                case Constants.EOPNOTSUPP:  // equal to ENOTSUP; see errno(3)
                    return Result.ENotSupported;

                default:
                    return Result.Invalid;
            }
        }

        internal static int ToErrno(this Result result)
        {
            switch (result)
            {
                case Result.Success:
                    return 0;
                case Result.Pending:
                    return Constants.EINPROGRESS;

                case Result.EInvalidArgs:
                    return Constants.EINVAL;
                case Result.EInvalidOperation:
                case Result.EVirtualizationInvalidOperation:
                    return Constants.EPERM;
                case Result.ENotSupported:
                    return Constants.EOPNOTSUPP;  // same value as ENOTSUP

                case Result.EDriverNotLoaded:
                    return Constants.ENODEV;
                case Result.EOutOfMemory:
                    return Constants.ENOMEM;
                case Result.EFileNotFound:
                case Result.EPathNotFound:
                    return Constants.ENOENT;
                case Result.EAccessDenied:
                    return Constants.EPERM;
                case Result.EInvalidHandle:
                    return Constants.EBADF;
                case Result.EIOError:
                    return Constants.EIO;
                case Result.EDirectoryNotEmpty:
                    return Constants.ENOTEMPTY;

                case Result.ENotYetImplemented:
                    return Constants.ENOSYS;

                // TODO(Linux): distinguish Result.Invalid, which a callback
                // might generate due to an incomplete internal implementation,
                // from EInvalidArgs, which a callback would use to indicate
                // it was passed invalid arguments and which therefore
                // maps correctly to EINVAL, while Result.Invalid does not
                case Result.Invalid:
                default:
                    return Constants.EINVAL;
            }
        }
    }
}
