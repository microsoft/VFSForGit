using System;
using System.Runtime.InteropServices;
using System.Text;
using Mono.Unix.Native;

namespace PrjFSLib.Linux
{
    public class VirtualizationInstance
    {
        public const int PlaceholderIdLength = 128;

        private Interop.ProjFS projfs;

        // References held to these delegates via class properties
        public virtual EnumerateDirectoryCallback OnEnumerateDirectory { get; set; }
        public virtual GetFileStreamCallback OnGetFileStream { get; set; }

        public virtual NotifyFileModified OnFileModified { get; set; }
        public virtual NotifyPreDeleteEvent OnPreDelete { get; set; }
        public virtual NotifyNewFileCreatedEvent OnNewFileCreated { get; set; }
        public virtual NotifyFileRenamedEvent OnFileRenamed { get; set; }
        public virtual NotifyHardLinkCreatedEvent OnHardLinkCreated { get; set; }

        public virtual Result StartVirtualizationInstance(
            string storageRootFullPath,
            string virtualizationRootFullPath,
            uint poolThreadCount)
        {
            if (this.projfs != null)
            {
                throw new InvalidOperationException();
            }

            Interop.ProjFS.Handlers handlers = new Interop.ProjFS.Handlers
            {
                HandleProjEvent = this.HandleProjEvent,
                HandleNotifyEvent = this.HandleNotifyEvent,
                HandlePermEvent = this.HandlePermEvent,
            };

            Interop.ProjFS fs = Interop.ProjFS.New(
                storageRootFullPath,
                virtualizationRootFullPath,
                handlers);

            if (fs == null)
            {
                return Result.Invalid;
            }

            if (fs.Start() != 0)
            {
                fs.Stop();
                return Result.Invalid;
            }

            this.projfs = fs;
            return Result.Success;
        }

        public virtual void StopVirtualizationInstance()
        {
            if (this.projfs == null)
            {
                return;
            }

            this.projfs.Stop();
            this.projfs = null;
        }

        public virtual Result WriteFileContents(
            int fd,
            byte[] bytes,
            uint byteCount)
        {
            unsafe
            {
                fixed (byte* buffer = bytes)
                {
                    return this.projfs.WriteFileContents(
                        fd,
                        (IntPtr)buffer,
                        byteCount);
                }
            }
        }

        public virtual Result DeleteFile(
            string relativePath,
            UpdateType updateFlags,
            out UpdateFailureCause failureCause)
        {
            /*
            UpdateFailureCause deleteFailureCause = UpdateFailureCause.NoFailure;
            Result result = Interop.PrjFSLib.DeleteFile(
                relativePath,
                updateFlags,
                ref deleteFailureCause);

            failureCause = deleteFailureCause;
            return result;
            */
            failureCause = UpdateFailureCause.NoFailure;
            return Result.ENotYetImplemented;
        }

        public virtual Result WritePlaceholderDirectory(
            string relativePath)
        {
            return this.projfs.CreateProjDir(relativePath, Convert.ToUInt16("0777", 8));
        }

        public virtual Result WritePlaceholderFile(
            string relativePath,
            byte[] providerId,
            byte[] contentId,
            ulong fileSize,
            ushort fileMode)
        {
            if (providerId.Length != PlaceholderIdLength ||
                contentId.Length != PlaceholderIdLength)
            {
                throw new ArgumentException();
            }

            return this.projfs.CreateProjFile(
                relativePath,
                fileSize,
                fileMode);
        }

        public virtual Result WriteSymLink(
            string relativePath,
            string symLinkTarget)
        {
            return this.projfs.CreateProjSymlink(
                relativePath,
                symLinkTarget);
        }

        public virtual Result UpdatePlaceholderIfNeeded(
            string relativePath,
            byte[] providerId,
            byte[] contentId,
            ulong fileSize,
            ushort fileMode,
            UpdateType updateFlags,
            out UpdateFailureCause failureCause)
        {
            /*
            if (providerId.Length != Interop.PrjFSLib.PlaceholderIdLength ||
                contentId.Length != Interop.PrjFSLib.PlaceholderIdLength)
            {
                throw new ArgumentException();
            }

            UpdateFailureCause updateFailureCause = UpdateFailureCause.NoFailure;
            Result result = Interop.PrjFSLib.UpdatePlaceholderFileIfNeeded(
                relativePath,
                providerId,
                contentId,
                fileSize,
                fileMode,
                updateFlags,
                ref updateFailureCause);

            failureCause = updateFailureCause;
            return result;
            */
            failureCause = UpdateFailureCause.NoFailure;
            return Result.ENotYetImplemented;
        }

        public virtual Result ReplacePlaceholderFileWithSymLink(
            string relativePath,
            string symLinkTarget,
            UpdateType updateFlags,
            out UpdateFailureCause failureCause)
        {
            /*
            UpdateFailureCause updateFailureCause = UpdateFailureCause.NoFailure;
            Result result = Interop.PrjFSLib.ReplacePlaceholderFileWithSymLink(
                relativePath,
                symLinkTarget,
                updateFlags,
                ref updateFailureCause);

            failureCause = updateFailureCause;
            return result;
            */
            failureCause = UpdateFailureCause.NoFailure;
            return Result.ENotYetImplemented;
        }

        public virtual Result CompleteCommand(
            ulong commandId,
            Result result)
        {
            throw new NotImplementedException();
        }

        public virtual Result ConvertDirectoryToPlaceholder(
            string relativeDirectoryPath)
        {
            throw new NotImplementedException();
        }

        private static string GetProcCmdline(int pid)
        {
            using (var stream = System.IO.File.OpenText(string.Format("/proc/{0}/cmdline", pid)))
            {
                string[] parts = stream.ReadToEnd().Split('\0');
                return parts.Length > 0 ? parts[0] : string.Empty;
            }
        }

        private static string PtrToStringUTF8(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero)
            {
                return null;
            }

            int length = (int)Stdlib.strlen(ptr);
            unsafe
            {
                return Encoding.UTF8.GetString((byte*)ptr, length);
            }
        }

        private int HandleProjEvent(ref Interop.ProjFS.Event ev)
        {
            string triggeringProcessName = GetProcCmdline(ev.Pid);
            Result result;

            if ((ev.Mask & Interop.ProjFS.Constants.PROJFS_ONDIR) != 0)
            {
                result = this.OnEnumerateDirectory(
                    commandId: 0,
                    relativePath: PtrToStringUTF8(ev.Path),
                    triggeringProcessId: ev.Pid,
                    triggeringProcessName: triggeringProcessName);
            }
            else
            {
                result = this.OnGetFileStream(
                    commandId: 0,
                    relativePath: PtrToStringUTF8(ev.Path),
                    providerId: new byte[PlaceholderIdLength],
                    contentId: new byte[PlaceholderIdLength],
                    triggeringProcessId: ev.Pid,
                    triggeringProcessName: triggeringProcessName,
                    fd: ev.Fd);
            }

            return result.ConvertResultToErrno();
        }

        private int HandleNonProjEvent(ref Interop.ProjFS.Event ev, bool perm)
        {
            NotificationType nt;

            if ((ev.Mask & Interop.ProjFS.Constants.PROJFS_DELETE_SELF) != 0)
            {
                nt = NotificationType.PreDelete;
            }
            else if ((ev.Mask & Interop.ProjFS.Constants.PROJFS_MOVE_SELF) != 0)
            {
                nt = NotificationType.FileRenamed;
            }
            else if ((ev.Mask & Interop.ProjFS.Constants.PROJFS_CREATE_SELF) != 0)
            {
                nt = NotificationType.NewFileCreated;
            }
            else
            {
                return 0;
            }

            string triggeringProcessName = GetProcCmdline(ev.Pid);

            Result result = this.OnNotifyOperation(
                commandId: 0,
                relativePath: PtrToStringUTF8(ev.Path),
                providerId: new byte[PlaceholderIdLength],
                contentId: new byte[PlaceholderIdLength],
                triggeringProcessId: ev.Pid,
                triggeringProcessName: triggeringProcessName,
                isDirectory: (ev.Mask & Interop.ProjFS.Constants.PROJFS_ONDIR) != 0,
                notificationType: nt);

            int ret = result.ConvertResultToErrno();

            if (perm)
            {
                if (ret == 0)
                {
                    ret = (int)Interop.ProjFS.Constants.PROJFS_ALLOW;
                }
                else if (ret == -(int)Errno.EPERM)
                {
                    ret = (int)Interop.ProjFS.Constants.PROJFS_DENY;
                }
            }

            return ret;
        }

        private int HandleNotifyEvent(ref Interop.ProjFS.Event ev)
        {
            return this.HandleNonProjEvent(ref ev, false);
        }

        private int HandlePermEvent(ref Interop.ProjFS.Event ev)
        {
            return this.HandleNonProjEvent(ref ev, true);
        }

        private Result OnNotifyOperation(
            ulong commandId,
            string relativePath,
            byte[] providerId,
            byte[] contentId,
            int triggeringProcessId,
            string triggeringProcessName,
            bool isDirectory,
            NotificationType notificationType)
        {
            switch (notificationType)
            {
                case NotificationType.PreDelete:
                    return this.OnPreDelete(relativePath, isDirectory);

                case NotificationType.FileModified:
                    this.OnFileModified(relativePath);
                    return Result.Success;

                case NotificationType.NewFileCreated:
                    this.OnNewFileCreated(relativePath, isDirectory);
                    return Result.Success;

                case NotificationType.FileRenamed:
                    this.OnFileRenamed(relativePath, isDirectory);
                    return Result.Success;

                case NotificationType.HardLinkCreated:
                    this.OnHardLinkCreated(relativePath);
                    return Result.Success;
            }

            return Result.ENotYetImplemented;
        }
    }
}
