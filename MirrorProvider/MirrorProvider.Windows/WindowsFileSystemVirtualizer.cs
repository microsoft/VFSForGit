using Microsoft.Windows.ProjFS;
using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace MirrorProvider.Windows
{
    public class WindowsFileSystemVirtualizer : FileSystemVirtualizer, IRequiredCallbacks
    {
        private VirtualizationInstance virtualizationInstance;
        private ConcurrentDictionary<Guid, ActiveEnumeration> activeEnumerations = new ConcurrentDictionary<Guid, ActiveEnumeration>();

        protected override StringComparison PathComparison => StringComparison.OrdinalIgnoreCase;
        protected override StringComparer PathComparer => StringComparer.OrdinalIgnoreCase;

        public override bool TryConvertVirtualizationRoot(string directory, out string error)
        {
            error = string.Empty;
            HResult result = VirtualizationInstance.MarkDirectoryAsVirtualizationRoot(directory, Guid.NewGuid());
            if (result != HResult.Ok)
            {
                error = result.ToString("F");
                return false;
            }

            return true;
        }

        public override bool TryStartVirtualizationInstance(Enlistment enlistment, out string error)
        {
            this.virtualizationInstance.OnQueryFileName = this.QueryFileName;
            this.virtualizationInstance.OnNotifyPreDelete = this.OnPreDelete;
            this.virtualizationInstance.OnNotifyNewFileCreated = this.OnNewFileCreated;
            this.virtualizationInstance.OnNotifyFileHandleClosedFileModifiedOrDeleted = this.OnFileModifiedOrDeleted;
            this.virtualizationInstance.OnNotifyFileRenamed = this.OnFileRenamed;
            this.virtualizationInstance.OnNotifyHardlinkCreated = this.OnHardlinkCreated;
            this.virtualizationInstance.OnNotifyFilePreConvertToFull = this.OnFilePreConvertToFull;

            uint threadCount = (uint)Environment.ProcessorCount * 2;

            NotificationMapping[] notificationMappings = new NotificationMapping[]
            {
                new NotificationMapping(
                    NotificationType.NewFileCreated |
                    NotificationType.PreDelete |
                    NotificationType.FileRenamed |
                    NotificationType.HardlinkCreated |
                    NotificationType.FileHandleClosedFileModified, 
                    string.Empty),
            };

            this.virtualizationInstance = new VirtualizationInstance(
                enlistment.SrcRoot,
                poolThreadCount: threadCount,
                concurrentThreadCount: threadCount,
                enableNegativePathCache: false,
                notificationMappings: notificationMappings);

            HResult result = this.virtualizationInstance.StartVirtualizing(this);

            if (result == HResult.Ok)
            {
                return base.TryStartVirtualizationInstance(enlistment, out error);
            }

            error = result.ToString("F");
            return false;
        }

        public HResult StartDirectoryEnumerationCallback(int commandId, Guid enumerationId, string relativePath, uint triggeringProcessId, string triggeringProcessImageFileName)
        {
            Console.WriteLine($"StartDirectoryEnumeration: `{relativePath}`, {enumerationId}");

            // On Windows, we have to sort the child items. The PrjFlt driver takes our list and merges it with
            // what is on disk, and it assumes that both lists are already sorted.
            ActiveEnumeration activeEnumeration = new ActiveEnumeration(
                this.GetChildItems(relativePath)
                .OrderBy(file => file.Name, PathComparer)
                .ToList());

            if (!this.activeEnumerations.TryAdd(enumerationId, activeEnumeration))
            {
                return HResult.InternalError;
            }

            return HResult.Ok;
        }

        public HResult EndDirectoryEnumerationCallback(Guid enumerationId)
        {
            Console.WriteLine($"EndDirectioryEnumeration: {enumerationId}");

            ActiveEnumeration activeEnumeration;
            if (!this.activeEnumerations.TryRemove(enumerationId, out activeEnumeration))
            {
                return HResult.InternalError;
            }

            return HResult.Ok;
        }

        public HResult GetDirectoryEnumerationCallback(
            int commandId,
            Guid enumerationId, 
            string filterFileName, 
            bool restartScan, 
            IDirectoryEnumerationResults results)
        {
            Console.WriteLine($"GetDiretoryEnumeration: {enumerationId}, {filterFileName}");

            try
            {
                ActiveEnumeration activeEnumeration = null;
                if (!this.activeEnumerations.TryGetValue(enumerationId, out activeEnumeration))
                {
                    return HResult.InternalError;
                }

                if (restartScan)
                {
                    activeEnumeration.RestartEnumeration(filterFileName);
                }
                else
                {
                    activeEnumeration.TrySaveFilterString(filterFileName);
                }

                bool entryAdded = false;

                HResult result = HResult.Ok;
                while (activeEnumeration.IsCurrentValid)
                {
                    ProjectedFileInfo fileInfo = activeEnumeration.Current;

                    DateTime now = DateTime.UtcNow;
                    if (results.Add(
                        fileName: fileInfo.Name,
                        fileSize: fileInfo.IsDirectory ? 0 : fileInfo.Size,
                        isDirectory: fileInfo.IsDirectory,
                        fileAttributes: fileInfo.IsDirectory ? FileAttributes.Directory : FileAttributes.Archive,
                        creationTime: now,
                        lastAccessTime: now,
                        lastWriteTime: now,
                        changeTime: now))
                    {
                        entryAdded = true;
                        activeEnumeration.MoveNext();
                    }
                    else
                    {
                        result = entryAdded ? HResult.Ok : HResult.InsufficientBuffer;
                        break;
                    }
                }

                return result;
            }
            catch (Win32Exception e)
            {
                return HResultFromWin32(e.NativeErrorCode);
            }
            catch (Exception)
            {
                return HResult.InternalError;
            }
        }

        public HResult GetPlaceholderInfoCallback(
            int commandId,
            string relativePath,
            uint triggeringProcessId,
            string triggeringProcessImageFileName)
        {
            Console.WriteLine($"GetPlaceholderInfoCallback: `{relativePath}`");

            ProjectedFileInfo fileInfo = this.GetFileInfo(relativePath);
            if (fileInfo == null)
            {
                return HResult.FileNotFound;
            }

            DateTime now = DateTime.UtcNow;
            HResult result = this.virtualizationInstance.WritePlaceholderInfo(
                Path.Combine(Path.GetDirectoryName(relativePath), fileInfo.Name),
                creationTime: now,
                lastAccessTime: now,
                lastWriteTime: now,
                changeTime: now,
                fileAttributes: fileInfo.IsDirectory ? FileAttributes.Directory : FileAttributes.Archive,
                endOfFile: fileInfo.Size,
                isDirectory: fileInfo.IsDirectory,
                contentId: new byte[] { 0 },
                providerId: new byte[] { 1 });

            if (result != HResult.Ok)
            {
                Console.WriteLine("WritePlaceholderInformation failed: " + result);
            }

            return result;
        }

        public HResult GetFileDataCallback(
            int commandId,
            string relativePath,
            ulong byteOffset,
            uint length,
            Guid streamGuid,
            byte[] contentId,
            byte[] providerId,
            uint triggeringProcessId,
            string triggeringProcessImageFileName)
        {
            Console.WriteLine($"GetFileDataCallback: `{relativePath}`");

            if (!this.FileExists(relativePath))
            {
                return HResult.FileNotFound;
            }

            try
            {
                const int bufferSize = 64 * 1024;
                using (IWriteBuffer writeBuffer = this.virtualizationInstance.CreateWriteBuffer(bufferSize))
                {
                    ulong writeOffset = 0;

                    FileSystemResult hydrateFileResult = this.HydrateFile(
                        relativePath,
                        bufferSize,
                        (readBuffer, bytesToCopy) =>
                        {
                            writeBuffer.Stream.Seek(0, SeekOrigin.Begin);
                            writeBuffer.Stream.Write(readBuffer, 0, (int)bytesToCopy);

                            HResult writeResult = this.virtualizationInstance.WriteFileData(streamGuid, writeBuffer, writeOffset, bytesToCopy);
                            if (writeResult != HResult.Ok)
                            {
                                Console.WriteLine("WriteFile faild: " + writeResult);
                                return false;
                            }

                            writeOffset += bytesToCopy;

                            return true;
                        });

                    if (hydrateFileResult != FileSystemResult.Success)
                    {
                        return HResult.InternalError;
                    }
                }
            }
            catch (IOException e)
            {
                Console.WriteLine("IOException in GetFileDataCallback: " + e.Message);
                return HResult.InternalError;
            }
            catch (UnauthorizedAccessException e)
            {
                Console.WriteLine("UnauthorizedAccessException in GetFileDataCallback: " + e.Message);
                return HResult.InternalError;
            }

            return HResult.Ok;
        }

        private HResult QueryFileName(string relativePath)
        {
            Console.WriteLine($"QueryFileName: `{relativePath}`");

            string parentDirectory = Path.GetDirectoryName(relativePath);
            string childName = Path.GetFileName(relativePath);
            if (this.GetChildItems(parentDirectory).Any(child => child.Name.Equals(childName, PathComparison)))
            {
                return HResult.Ok;
            }

            return HResult.FileNotFound;
        }

        private bool OnPreDelete(string relativePath, bool isDirectory, uint triggeringProcessId, string triggeringProcessImageFileName)
        {
            Console.WriteLine($"OnPreDelete (isDirectory: {isDirectory}): {relativePath}, triggeringProcessId: {triggeringProcessId}, triggeringProcessImageFileName: {triggeringProcessImageFileName}");
            return true;
        }

        private void OnNewFileCreated(
            string relativePath,
            bool isDirectory,
            uint triggeringProcessId,
            string triggeringProcessImageFileName,
            out NotificationType notificationMask)
        {
            notificationMask = NotificationType.UseExistingMask;
            Console.WriteLine($"OnNewFileCreated (isDirectory: {isDirectory}): {relativePath}, triggeringProcessId: {triggeringProcessId}, triggeringProcessImageFileName: {triggeringProcessImageFileName}");
        }

        private void OnFileModifiedOrDeleted(string relativePath, bool isDirectory, bool isFileModified, bool isFileDeleted, uint triggeringProcessId, string triggeringProcessImageFileName)
        {
            // To keep WindowsFileSystemVirtualizer in sync with MacFileSystemVirtualizer we're only registering for 
            // NotificationType.FileHandleClosedFileModified and so this method will only be called for modifications.  
            // Once MacFileSystemVirtualizer supports delete notifications we'll register for
            // NotificationType.FileHandleClosedFileDeleted and this method will be called for both modifications and deletions.
            Console.WriteLine($"OnFileModifiedOrDeleted: `{relativePath}`, isDirectory: {isDirectory}, isModfied: {isFileModified}, isDeleted: {isFileDeleted}, triggeringProcessId: {triggeringProcessId}, triggeringProcessImageFileName: {triggeringProcessImageFileName}");
        }

        private void OnFileRenamed(
            string relativeSourcePath,
            string relativeDestinationPath,
            bool isDirectory,
            uint triggeringProcessId,
            string triggeringProcessImageFileName,
            out NotificationType notificationMask)
        {
            notificationMask = NotificationType.UseExistingMask;
            Console.WriteLine($"OnFileRenamed (isDirectory: {isDirectory}), relativeSourcePath: {relativeSourcePath}, relativeDestinationPath: {relativeDestinationPath}, triggeringProcessId: {triggeringProcessId}, triggeringProcessImageFileName: {triggeringProcessImageFileName}");
        }

        private void OnHardlinkCreated(
            string relativeExistingFilePath,
            string relativeNewLinkFilePath,
            uint triggeringProcessId,
            string triggeringProcessImageFileName)
        {
            Console.WriteLine($"OnHardlinkCreated, relativeExistingFilePath: {relativeExistingFilePath}, relativeNewLinkFilePath: {relativeNewLinkFilePath}, triggeringProcessId: {triggeringProcessId}, triggeringProcessImageFileName: {triggeringProcessImageFileName}");
        }

        private bool OnFilePreConvertToFull(string virtualPath, uint triggeringProcessId, string triggeringProcessImageFileName)
        {
            Console.WriteLine($"OnPreConvertToFull (virtualPath: {virtualPath}), triggeringProcessId: {triggeringProcessId}, triggeringProcessImageFileName: {triggeringProcessImageFileName}");
            return true;
        }

        // TODO: Add this to the ProjFS API
        private static HResult HResultFromWin32(int win32error)
        {
            // HRESULT_FROM_WIN32(unsigned long x) { return (HRESULT)(x) <= 0 ? (HRESULT)(x) : (HRESULT) (((x) & 0x0000FFFF) | (FACILITY_WIN32 << 16) | 0x80000000);}

            const int FacilityWin32 = 7;
            return win32error <= 0 ? (HResult)win32error : (HResult)unchecked((win32error & 0x0000FFFF) | (FacilityWin32 << 16) | 0x80000000);
        }
    }
}
