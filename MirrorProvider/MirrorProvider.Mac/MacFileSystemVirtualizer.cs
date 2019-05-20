using PrjFSLib.Mac;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace MirrorProvider.Mac
{
    public class MacFileSystemVirtualizer : FileSystemVirtualizer
    {
        private VirtualizationInstance virtualizationInstance = new VirtualizationInstance();

        protected override StringComparison PathComparison => StringComparison.OrdinalIgnoreCase;
        protected override StringComparer PathComparer => StringComparer.OrdinalIgnoreCase;

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
            this.virtualizationInstance.OnLogError = this.OnLogError;
            this.virtualizationInstance.OnLogWarning = this.OnLogWarning;
            this.virtualizationInstance.OnLogInfo = this.OnLogInfo;
            this.virtualizationInstance.OnFileModified = this.OnFileModified;
            this.virtualizationInstance.OnPreDelete = this.OnPreDelete;
            this.virtualizationInstance.OnNewFileCreated = this.OnNewFileCreated;
            this.virtualizationInstance.OnFileRenamed = this.OnFileRenamed;
            this.virtualizationInstance.OnHardLinkCreated = this.OnHardLinkCreated;
            this.virtualizationInstance.OnFilePreConvertToFull = this.OnFilePreConvertToFull;

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
            Console.WriteLine($"OnEnumerateDirectory({commandId}, '{relativePath}', {triggeringProcessId}, {triggeringProcessName})");

            try
            {
                if (!this.DirectoryExists(relativePath))
                {
                    return Result.EFileNotFound;
                }

                foreach (ProjectedFileInfo child in this.GetChildItems(relativePath))
                {
                    if (child.Type == ProjectedFileInfo.FileType.Directory)
                    {
                        Result result = this.virtualizationInstance.WritePlaceholderDirectory(
                            Path.Combine(relativePath, child.Name));

                        if (result != Result.Success)
                        {
                            Console.WriteLine($"WritePlaceholderDirectory failed: {result}");
                            return result;
                        }
                    }
                    else if (child.Type == ProjectedFileInfo.FileType.SymLink)
                    {
                        string childRelativePath = Path.Combine(relativePath, child.Name);

                        string symLinkTarget;
                        if (this.TryGetSymLinkTarget(childRelativePath, out symLinkTarget))
                        {
                            Result result = this.virtualizationInstance.WriteSymLink(
                                childRelativePath,
                                symLinkTarget);

                            if (result != Result.Success)
                            {
                                Console.WriteLine($"WriteSymLink failed: {result}");
                                return result;
                            }
                        }
                        else
                        {
                            return Result.EIOError;
                        }
                    }
                    else
                    {
                        // The MirrorProvider marks every file as executable (mode 755), but this is just a shortcut to avoid the pain of
                        // having to p/invoke to determine if the original file is exectuable or not.
                        // A real provider will have to get this information from its data source. For example, GVFS gets this info 
                        // out of the git index along with all the other info for projecting files.
                        UInt16 fileMode = Convert.ToUInt16("755", 8);

                        Result result = this.virtualizationInstance.WritePlaceholderFile(
                            Path.Combine(relativePath, child.Name),
                            providerId: ToVersionIdByteArray(1),
                            contentId: ToVersionIdByteArray(0),
                            fileMode: fileMode);
                        if (result != Result.Success)
                        {
                            Console.WriteLine($"WritePlaceholderFile failed: {result}");
                            return result;
                        }
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
            Console.WriteLine($"OnGetFileStream({commandId}, '{relativePath}', {contentId.Length}/{contentId[0]}:{contentId[1]}, {providerId.Length}/{providerId[0]}:{providerId[1]}, {triggeringProcessId}, {triggeringProcessName}, 0x{fileHandle.ToInt64():X})");

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

        private void OnLogError(string errorMessage)
        {
            Console.WriteLine($"OnLogError: {errorMessage}");
        }

        private void OnLogWarning(string warningMessage)
        {
            Console.WriteLine($"OnLogWarning: {warningMessage}");
        }
        private void OnLogInfo(string infoMessage)
        {
            Console.WriteLine($"OnLogInfo: {infoMessage}");
        }

        private void OnFileModified(string relativePath)
        {
            Console.WriteLine($"OnFileModified: {relativePath}");
        }

        private Result OnPreDelete(string relativePath, bool isDirectory)
        {
            Console.WriteLine($"OnPreDelete (isDirectory: {isDirectory}): {relativePath}");
            return Result.Success;
        }

        private void OnNewFileCreated(string relativePath, bool isDirectory)
        {
            Console.WriteLine($"OnNewFileCreated (isDirectory: {isDirectory}): {relativePath}");
        }

        private void OnFileRenamed(string relativeDestinationPath, bool isDirectory)
        {
            Console.WriteLine($"OnFileRenamed (isDirectory: {isDirectory}) destination: {relativeDestinationPath}");
        }

        private void OnHardLinkCreated(string existingRelativePath, string relativeNewLinkPath)
        {
            Console.WriteLine($"OnHardLinkCreated {relativeNewLinkPath} from {existingRelativePath}");
        }

        private Result OnFilePreConvertToFull(string relativePath)
        {
            Console.WriteLine($"OnFilePreConvertToFull: {relativePath}");
            return Result.Success;
        }

        private bool TryGetSymLinkTarget(string relativePath, out string symLinkTarget)
        {
            string fullPathInMirror = this.GetFullPathInMirror(relativePath);
            symLinkTarget = MacNative.ReadLink(fullPathInMirror, out int error);
            if (symLinkTarget == null)
            {
                Console.WriteLine($"GetSymLinkTarget failed: {error}");
                return false;
            }

            if (symLinkTarget.StartsWith(this.Enlistment.MirrorRoot, PathComparison))
            {
                // Link target is an absolute path inside the MirrorRoot.  
                // The target needs to be adjusted to point inside the src root
                symLinkTarget = Path.Combine(
                    this.Enlistment.SrcRoot.TrimEnd(Path.DirectorySeparatorChar),
                    symLinkTarget.Substring(this.Enlistment.MirrorRoot.Length).TrimStart(Path.DirectorySeparatorChar));
            }

            return true;
        }

        private static byte[] ToVersionIdByteArray(byte version)
        {
            byte[] bytes = new byte[VirtualizationInstance.PlaceholderIdLength];
            bytes[0] = version;

            return bytes;
        }
    }
}
