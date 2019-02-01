using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace GVFS.Platform.Windows
{
    public partial class WindowsFileSystem : IPlatformFileSystem
    {
        public bool SupportsFileMode { get; } = false;

        public void FlushFileBuffers(string path)
        {
            NativeMethods.FlushFileBuffers(path);
        }

        public void MoveAndOverwriteFile(string sourceFileName, string destinationFilename)
        {
            NativeMethods.MoveFile(
                sourceFileName,
                destinationFilename,
                NativeMethods.MoveFileFlags.MoveFileReplaceExisting);
        }

        public void CreateHardLink(string newFileName, string existingFileName)
        {
            NativeMethods.CreateHardLink(newFileName, existingFileName);
        }

        public void ChangeMode(string path, ushort mode)
        {
        }

        public bool TryGetNormalizedPath(string path, out string normalizedPath, out string errorMessage)
        {
            return WindowsFileSystem.TryGetNormalizedPathImplementation(path, out normalizedPath, out errorMessage);
        }

        public bool HydrateFile(string fileName, byte[] buffer)
        {
            return NativeFileReader.TryReadFirstByteOfFile(fileName, buffer);
        }

        public bool IsExecutable(string fileName)
        {
            string fileExtension = Path.GetExtension(fileName);
            return string.Equals(fileExtension, ".exe", StringComparison.OrdinalIgnoreCase);
        }

        public bool IsSocket(string fileName)
        {
            return false;
        }

        public bool TryCreateAndConfigureDirectoryWithAdminOnlyModify(ITracer tracer, string directoryPath, out string error)
        {
            try
            {
                DirectorySecurity directorySecurity;
                if (Directory.Exists(directoryPath))
                {
                    directorySecurity = Directory.GetAccessControl(directoryPath);
                }
                else
                {
                    directorySecurity = new DirectorySecurity();
                }

                // Protect the access rules from inheritance
                directorySecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

                // Remove any rules already set for the directory
                AuthorizationRuleCollection currentRules = directorySecurity.GetAccessRules(includeExplicit: true, includeInherited: true, targetType: typeof(NTAccount));
                foreach (AuthorizationRule authorizationRule in currentRules)
                {
                    FileSystemAccessRule fileSystemRule = authorizationRule as FileSystemAccessRule;
                    if (fileSystemRule != null)
                    {
                        directorySecurity.RemoveAccessRule(fileSystemRule);
                    }
                }

                // All users gets read access
                SecurityIdentifier allUsers = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
                directorySecurity.AddAccessRule(
                    new FileSystemAccessRule(
                        allUsers,
                        FileSystemRights.Read,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                        PropagationFlags.None,
                        AccessControlType.Allow));

                // Only administrators have Execute/Modify/Delete access
                SecurityIdentifier administratorUsers = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                directorySecurity.AddAccessRule(
                    new FileSystemAccessRule(
                        administratorUsers,
                        FileSystemRights.ReadAndExecute | FileSystemRights.Modify | FileSystemRights.Delete,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                        PropagationFlags.None,
                        AccessControlType.Allow));

                Directory.CreateDirectory(directoryPath, directorySecurity);

                // Ensure the ACLs are set correctly if the directory already existed
                Directory.SetAccessControl(directoryPath, directorySecurity);
            }
            catch (IOException e)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Exception", e.ToString());
                tracer.RelatedError(metadata, $"{nameof(this.TryCreateAndConfigureDirectoryWithAdminOnlyModify)}: IOException while creating/configuring directory");

                error = e.Message;
                return false;
            }
            catch (SystemException e)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Exception", e.ToString());
                tracer.RelatedError(metadata, $"{nameof(this.TryCreateAndConfigureDirectoryWithAdminOnlyModify)}: SystemException while creating/configuring directory");

                error = e.Message;
                return false;
            }

            error = null;
            return true;
        }

        private class NativeFileReader
        {
            private const uint GenericRead = 0x80000000;
            private const uint OpenExisting = 3;

            public static bool TryReadFirstByteOfFile(string fileName, byte[] buffer)
            {
                using (SafeFileHandle handle = Open(fileName))
                {
                    if (!handle.IsInvalid)
                    {
                        return ReadOneByte(handle, buffer);
                    }
                }

                return false;
            }

            private static SafeFileHandle Open(string fileName)
            {
                return CreateFile(fileName, GenericRead, (uint)(FileShare.ReadWrite | FileShare.Delete), 0, OpenExisting, 0, 0);
            }

            private static bool ReadOneByte(SafeFileHandle handle, byte[] buffer)
            {
                int bytesRead = 0;
                return ReadFile(handle, buffer, 1, ref bytesRead, 0);
            }

            [DllImport("kernel32", SetLastError = true, ThrowOnUnmappableChar = true, CharSet = CharSet.Unicode)]
            private static extern SafeFileHandle CreateFile(
                string fileName,
                uint desiredAccess,
                uint shareMode,
                uint securityAttributes,
                uint creationDisposition,
                uint flagsAndAttributes,
                int hemplateFile);

            [DllImport("kernel32", SetLastError = true)]
            private static extern bool ReadFile(
                SafeFileHandle file,
                [Out] byte[] buffer,
                int numberOfBytesToRead,
                ref int numberOfBytesRead,
                int overlapped);
        }
    }
}
