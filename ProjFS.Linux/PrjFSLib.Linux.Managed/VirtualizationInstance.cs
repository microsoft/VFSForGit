using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using PrjFSLib.Linux.Interop;
using static PrjFSLib.Linux.Interop.Errno;

namespace PrjFSLib.Linux
{
    public class VirtualizationInstance
    {
        public const int PlaceholderIdLength = 128;

        private ProjFS projfs;
        private int currentProcessId = Process.GetCurrentProcess().Id;
        private string virtualizationRoot;

        // We must hold a reference to the delegates to prevent garbage collection
        private ProjFS.EventHandler preventGCOnProjEventDelegate;
        private ProjFS.EventHandler preventGCOnNotifyEventDelegate;
        private ProjFS.EventHandler preventGCOnPermEventDelegate;

        // References held to these delegates via class properties
        public virtual EnumerateDirectoryCallback OnEnumerateDirectory { get; set; }
        public virtual GetFileStreamCallback OnGetFileStream { get; set; }
        public virtual LogErrorCallback OnLogError { get; set; }
        public virtual LogWarningCallback OnLogWarning { get; set; }
        public virtual LogInfoCallback OnLogInfo { get; set; }

        public virtual NotifyFileModified OnFileModified { get; set; }
        public virtual NotifyFilePreConvertToFullEvent OnFilePreConvertToFull { get; set; }
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

            ProjFS.Handlers handlers = new ProjFS.Handlers
            {
                HandleProjEvent = this.preventGCOnProjEventDelegate = new ProjFS.EventHandler(this.HandleProjEvent),
                HandleNotifyEvent = this.preventGCOnNotifyEventDelegate = new ProjFS.EventHandler(this.HandleNotifyEvent),
                HandlePermEvent = this.preventGCOnPermEventDelegate = new ProjFS.EventHandler(this.HandlePermEvent)
            };

            ProjFS fs = ProjFS.New(
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
            this.virtualizationRoot = virtualizationRootFullPath;
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
            if (!NativeFileWriter.TryWrite(fd, bytes, byteCount))
            {
                return Result.EIOError;
            }

            return Result.Success;
        }

        public virtual Result DeleteFile(
            string relativePath,
            UpdateType updateFlags,
            out UpdateFailureCause failureCause)
        {
            failureCause = UpdateFailureCause.NoFailure;
            if (relativePath == string.Empty)
            {
                // mount point directory can not be deleted, so ignore
                return Result.Success;
            }

            string fullPath = Path.Combine(this.virtualizationRoot, relativePath);
            try
            {
                if (Directory.Exists(fullPath))
                {
                    Directory.Delete(fullPath);
                }
                else
                {
                    // TODO(Linux): try to handle races with hydration?
                    ProjectionState state;
                    Result result = this.projfs.GetProjState(relativePath, out state);

                    if (result == Result.EPathNotFound)
                    {
                        return Result.Success;
                    }
                    else if (result == Result.EAccessDenied || (result == Result.Success && state == ProjectionState.Full))
                    {
                        /* TODO(Linux): return EAccessDenied unless EPERM
                         * (EPERM is returned when path is not a file or dir,
                         *  which we want to treat as a full file)
                         */
                        failureCause = UpdateFailureCause.DirtyData;
                        return Result.EVirtualizationInvalidOperation;
                    }
                    else if (result != Result.Success)
                    {
                        return result;
                    }

                    File.Delete(fullPath);
                }
            }
            catch (IOException ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
            {
                return Result.Success;
            }
            catch (IOException)
            {
                // TODO(Linux): return EDirectoryNotEmpty on ENOTEMPTY
                return Result.EIOError;
            }
            catch (UnauthorizedAccessException)
            {
                // TODO(Linux): set UpdateFailureCause.ReadOnly on EACCES
                return Result.EAccessDenied;
            }

            return Result.Success;
        }

        public virtual Result WritePlaceholderDirectory(
            string relativePath)
        {
            return this.projfs.CreateProjDir(relativePath, Convert.ToUInt32("777", 8));
        }

        public virtual Result WritePlaceholderFile(
            string relativePath,
            byte[] providerId,
            byte[] contentId,
            ulong fileSize,
            uint fileMode)
        {
            if (providerId.Length != PlaceholderIdLength ||
                contentId.Length != PlaceholderIdLength)
            {
                throw new ArgumentException();
            }

            return this.projfs.CreateProjFile(
                relativePath,
                fileSize,
                fileMode,
                providerId,
                contentId);
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
            uint fileMode,
            UpdateType updateFlags,
            out UpdateFailureCause failureCause)
        {
            if (providerId.Length != PlaceholderIdLength ||
                contentId.Length != PlaceholderIdLength)
            {
                throw new ArgumentException();
            }

            Result result = this.DeleteFile(relativePath, updateFlags, out failureCause);
            if (result != Result.Success)
            {
                return result;
            }

            // TODO(Linux): try to handle races with hydration?
            failureCause = UpdateFailureCause.NoFailure;
            return this.WritePlaceholderFile(relativePath, providerId, contentId, fileSize, fileMode);
        }

        public virtual Result ReplacePlaceholderFileWithSymLink(
            string relativePath,
            string symLinkTarget,
            UpdateType updateFlags,
            out UpdateFailureCause failureCause)
        {
            Result result = this.DeleteFile(relativePath, updateFlags, out failureCause);
            if (result != Result.Success)
            {
                return result;
            }

            // TODO(Linux): try to handle races with hydration?
            failureCause = UpdateFailureCause.NoFailure;
            return this.WriteSymLink(relativePath, symLinkTarget);
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
            try
            {
                using (var stream = File.OpenText($"/proc/{pid}/cmdline"))
                {
                    string[] parts = stream.ReadToEnd().Split('\0');
                    return parts.Length > 0 ? parts[0] : string.Empty;
                }
            }
            catch (IOException ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
            {
                // process with given pid may have exited; nothing to be done
                return string.Empty;
            }
        }

        // TODO(Linux): replace with netstandard2.1 Marshal.PtrToStringUTF8()
        private static string PtrToStringUTF8(IntPtr ptr)
        {
            return Marshal.PtrToStringAnsi(ptr);
        }

        private bool IsProviderEvent(ProjFS.Event ev)
        {
            return (ev.Pid == this.currentProcessId);
        }

        private int HandleProjEvent(ref ProjFS.Event ev)
        {
            // ignore events triggered by own process to prevent deadlocks
            if (this.IsProviderEvent(ev))
            {
                return 0;
            }

            string triggeringProcessName = GetProcCmdline(ev.Pid);
            string relativePath = PtrToStringUTF8(ev.Path);

            Result result;

            if ((ev.Mask & ProjFS.Constants.PROJFS_ONDIR) != 0)
            {
                result = this.OnEnumerateDirectory(
                    commandId: 0,
                    relativePath: relativePath,
                    triggeringProcessId: ev.Pid,
                    triggeringProcessName: triggeringProcessName);
            }
            else
            {
                byte[] providerId = new byte[PlaceholderIdLength];
                byte[] contentId = new byte[PlaceholderIdLength];

                result = this.projfs.GetProjAttrs(
                    relativePath,
                    providerId,
                    contentId);

                if (result == Result.Success)
                {
                    result = this.OnGetFileStream(
                        commandId: 0,
                        relativePath: relativePath,
                        providerId: providerId,
                        contentId: contentId,
                        triggeringProcessId: ev.Pid,
                        triggeringProcessName: triggeringProcessName,
                        fd: ev.Fd);
                }
            }

            return -result.ToErrno();
        }

        private int HandleNonProjEvent(ref ProjFS.Event ev, bool perm)
        {
            // ignore events triggered by own process to prevent deadlocks
            if (this.IsProviderEvent(ev))
            {
                return 0;
            }

            bool isLink = (ev.Mask & ProjFS.Constants.PROJFS_ONLINK) != 0;
            NotificationType nt;

            if ((ev.Mask & ProjFS.Constants.PROJFS_DELETE_PERM) != 0)
            {
                nt = NotificationType.PreDelete;
            }
            else if ((ev.Mask & ProjFS.Constants.PROJFS_CLOSE_WRITE) != 0)
            {
                nt = NotificationType.FileModified;
            }
            else if ((ev.Mask & ProjFS.Constants.PROJFS_CREATE) != 0 && !isLink)
            {
                nt = NotificationType.NewFileCreated;
            }
            else if ((ev.Mask & ProjFS.Constants.PROJFS_MOVE) != 0)
            {
                nt = NotificationType.FileRenamed;
            }
            else if ((ev.Mask & ProjFS.Constants.PROJFS_CREATE) != 0 && isLink)
            {
                nt = NotificationType.HardLinkCreated;
            }
            else if ((ev.Mask & ProjFS.Constants.PROJFS_OPEN_PERM) != 0)
            {
                nt = NotificationType.PreConvertToFull;
            }
            else
            {
                return 0;
            }

            bool isDirectory = (ev.Mask & ProjFS.Constants.PROJFS_ONDIR) != 0;
            string triggeringProcessName = GetProcCmdline(ev.Pid);
            byte[] providerId = new byte[PlaceholderIdLength];
            byte[] contentId = new byte[PlaceholderIdLength];
            string relativePath = PtrToStringUTF8(ev.Path);
            Result result = Result.Success;

            if (!isDirectory)
            {
                string currentRelativePath = relativePath;

                // TODO(Linux): can other intermediate file ops race us here?
                if (nt == NotificationType.FileRenamed || nt == NotificationType.HardLinkCreated)
                {
                    currentRelativePath = PtrToStringUTF8(ev.TargetPath);

                    if (nt == NotificationType.HardLinkCreated)
                    {
                        relativePath = currentRelativePath;
                    }
                }

                result = this.projfs.GetProjAttrs(
                    currentRelativePath,
                    providerId,
                    contentId);
            }

            if (result == Result.Success)
            {
                result = this.OnNotifyOperation(
                    commandId: 0,
                    relativePath: relativePath,
                    providerId: providerId,
                    contentId: contentId,
                    triggeringProcessId: ev.Pid,
                    triggeringProcessName: triggeringProcessName,
                    isDirectory: isDirectory,
                    notificationType: nt);
            }

            int ret = -result.ToErrno();

            if (perm)
            {
                if (ret == 0)
                {
                    ret = (int)ProjFS.Constants.PROJFS_ALLOW;
                }
                else if (ret == -Errno.Constants.EPERM)
                {
                    ret = (int)ProjFS.Constants.PROJFS_DENY;
                }
            }

            return ret;
        }

        private int HandleNotifyEvent(ref ProjFS.Event ev)
        {
            return this.HandleNonProjEvent(ref ev, false);
        }

        private int HandlePermEvent(ref ProjFS.Event ev)
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

                case NotificationType.PreConvertToFull:
                    return this.OnFilePreConvertToFull(relativePath);
            }

            return Result.ENotYetImplemented;
        }

        private static unsafe class NativeFileWriter
        {
            public static bool TryWrite(int fd, byte[] bytes, uint byteCount)
            {
                 fixed (byte* bytesPtr = bytes)
                 {
                     byte* bytesIndexPtr = bytesPtr;

                     while (byteCount > 0)
                     {
                        long res = Write(fd, bytesIndexPtr, byteCount);
                        if (res == -1)
                        {
                            return false;
                        }

                        bytesIndexPtr += res;
                        byteCount -= (uint)res;
                    }
                }

                return true;
            }

            [DllImport("libc", EntryPoint = "write", SetLastError = true)]
            private static extern long Write(int fd, byte* buf, ulong count);
        }
    }
}
