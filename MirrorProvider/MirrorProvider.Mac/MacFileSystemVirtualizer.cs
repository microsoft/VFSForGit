﻿using PrjFSLib.Mac;
using System;
using System.IO;
using System.Threading;

namespace MirrorProvider.Mac
{
    public class MacFileSystemVirtualizer : FileSystemVirtualizer
    {
        private VirtualizationInstance virtualizationInstance = new VirtualizationInstance();

        public override bool TryConvertVirtualizationRoot(string directory, out string error)
        {
            Result result = VirtualizationInstance.ConvertDirectoryToVirtualizationRoot(directory);

            error = result.ToString();
            return result == Result.Success;
        }

        public override bool TryStartVirtualizationInstance(Enlistment enlistment, out string error)
        {
            this.virtualizationInstance.OnEnumerateDirectory = this.OnEnumerateDirectory;
            this.virtualizationInstance.OnGetFileStream = this.OnGetFileStream;
            this.virtualizationInstance.OnFileModified = this.OnFileModified;
            this.virtualizationInstance.OnPreDelete = this.OnPreDelete;
            this.virtualizationInstance.OnNewFileCreated = this.OnNewFileCreated;
            this.virtualizationInstance.OnFileRenamed = this.OnFileRenamed;
            this.virtualizationInstance.OnHardLinkCreated = this.OnHardLinkCreated;

            Result result = this.virtualizationInstance.StartVirtualizationInstance(
                enlistment.SrcRoot,
                poolThreadCount: (uint)Environment.ProcessorCount * 2);

            if (result == Result.Success)
            {
                return base.TryStartVirtualizationInstance(enlistment, out error);
            }
            else
            {
                error = result.ToString();
                return false;
            }
        }

        private Result OnEnumerateDirectory(
            ulong commandId,
            string relativePath,
            int triggeringProcessId,
            string triggeringProcessName)
        {
            try
            {
                if (!this.DirectoryExists(relativePath))
                {
                    return Result.EFileNotFound;
                }

                foreach (ProjectedFileInfo child in this.GetChildItems(relativePath))
                {
                    if (child.IsDirectory)
                    {
                        Result result = this.virtualizationInstance.WritePlaceholderDirectory(
                            Path.Combine(relativePath, child.Name));

                        if (result != Result.Success)
                        {
                            Console.WriteLine($"WritePlaceholderDirectory failed: {result}");
                            return result;
                        }
                    }
                    else
                    {
                        // The MirrorProvider marks every file as executable (mode 755), but this is just a shortcut to avoid the pain of
                        // having to p/invoke to determine if the original file is exectuable or not.
                        // A real provider will have to get this information from its data source. For example, GVFS gets this info 
                        // out of the git index along with all the other info for projecting files.
                        UInt16 fileMode = Convert.ToUInt16("775", 8);

                        Result result = this.virtualizationInstance.WritePlaceholderFile(
                            Path.Combine(relativePath, child.Name),
                            providerId: ToVersionIdByteArray(1),
                            contentId: ToVersionIdByteArray(0),
                            fileSize: (ulong)child.Size,
                            fileMode: fileMode);
                        if (result != Result.Success)
                        {
                            Console.WriteLine($"WritePlaceholderFile failed: {result}");
                            return result;
                        }
                    }

                    if (relativePath == "A")
                    {
                        Thread.Sleep(TimeSpan.FromHours(1));
                    }
                }
            }
            catch (IOException e)
            {
                Console.WriteLine($"IOException in OnEnumerateDirectory: {e.Message}");
                return Result.EIOError;
            }

            return Result.Success;
        }

        private Result OnGetFileStream(
            ulong commandId,
            string relativePath,
            byte[] providerId,
            byte[] contentId,
            int triggeringProcessId,
            string triggeringProcessName,
            IntPtr fileHandle)
        {
            if (!this.FileExists(relativePath))
            {
                return Result.EFileNotFound;
            }

            try
            {
                const int bufferSize = 4096;
                FileSystemResult hydrateFileResult = this.HydrateFile(
                    relativePath,
                    bufferSize,
                    (buffer, bytesToCopy) =>
                    {
                        Result result = this.virtualizationInstance.WriteFileContents(
                            fileHandle,
                            buffer,
                            (uint)bytesToCopy);
                        if (result != Result.Success)
                        {
                        Console.WriteLine($"WriteFileContents failed: {result}");
                            return false;
                        }

                        return true;
                    });

                if (hydrateFileResult != FileSystemResult.Success)
                {
                    return Result.EIOError;
                }
            }
            catch (IOException e)
            {
                Console.WriteLine($"IOException in OnGetFileStream: {e.Message}");
                return Result.EIOError;
            }

            return Result.Success;
        }

        private void OnFileModified(string relativePath)
        {
        }

        private Result OnPreDelete(string relativePath, bool isDirectory)
        {
            return Result.Success;
        }

        private void OnNewFileCreated(string relativePath, bool isDirectory)
        {
        }

        private void OnFileRenamed(string relativeDestinationPath, bool isDirectory)
        {
        }

        private void OnHardLinkCreated(string relativeNewLinkPath)
        {
        }

        private static byte[] ToVersionIdByteArray(byte version)
        {
            byte[] bytes = new byte[VirtualizationInstance.PlaceholderIdLength];
            bytes[0] = version;

            return bytes;
        }
    }
}
