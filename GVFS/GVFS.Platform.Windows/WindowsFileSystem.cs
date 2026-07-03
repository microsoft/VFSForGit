using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace GVFS.Platform.Windows
{
    public partial class WindowsFileSystem : IPlatformFileSystem
    {
        public bool SupportsFileMode { get; } = false;

        /// <summary>
        /// Adds a new FileSystemAccessRule granting read (and optionally modify) access for all users.
        /// </summary>
        /// <param name="directorySecurity">DirectorySecurity to which a FileSystemAccessRule will be added.</param>
        /// <param name="grantUsersModifyPermissions">
        /// True if all users should be given modify access, false if users should only be allowed read access
        /// </param>
        public static void AddUsersAccessRulesToDirectorySecurity(DirectorySecurity directorySecurity, bool grantUsersModifyPermissions)
        {
            SecurityIdentifier allUsers = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            FileSystemRights rights = FileSystemRights.Read;
            if (grantUsersModifyPermissions)
            {
                rights = rights | FileSystemRights.Modify;
            }

            // InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit -> ACE is inherited by child directories and files
            // PropagationFlags.None -> Standard propagation rules, settings are applied to the directory and its children
            // AccessControlType.Allow -> Rule is used to allow access to an object
            directorySecurity.AddAccessRule(
                new FileSystemAccessRule(
                    allUsers,
                    rights,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
        }

        /// <summary>
        /// Adds a new FileSystemAccessRule granting read/exceute/modify/delete access for administrators.
        /// </summary>
        /// <param name="directorySecurity">DirectorySecurity to which a FileSystemAccessRule will be added.</param>
        public static void AddAdminAccessRulesToDirectorySecurity(DirectorySecurity directorySecurity)
        {
            SecurityIdentifier administratorUsers = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

            // InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit -> ACE is inherited by child directories and files
            // PropagationFlags.None -> Standard propagation rules, settings are applied to the directory and its children
            // AccessControlType.Allow -> Rule is used to allow access to an object
            directorySecurity.AddAccessRule(
                new FileSystemAccessRule(
                    administratorUsers,
                    FileSystemRights.ReadAndExecute | FileSystemRights.Modify | FileSystemRights.Delete,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
        }

        /// <summary>
        /// Removes all FileSystemAccessRules from specified DirectorySecurity
        /// </summary>
        /// <param name="directorySecurity">DirectorySecurity from which to remove FileSystemAccessRules</param>
        public static void RemoveAllFileSystemAccessRulesFromDirectorySecurity(DirectorySecurity directorySecurity)
        {
            AuthorizationRuleCollection currentRules = directorySecurity.GetAccessRules(includeExplicit: true, includeInherited: true, targetType: typeof(NTAccount));
            foreach (AuthorizationRule authorizationRule in currentRules)
            {
                FileSystemAccessRule fileSystemRule = authorizationRule as FileSystemAccessRule;
                if (fileSystemRule != null)
                {
                    directorySecurity.RemoveAccessRule(fileSystemRule);
                }
            }
        }

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

        public void SetDirectoryLastWriteTime(string path, DateTime lastWriteTime, out bool directoryExists)
        {
            NativeMethods.SetDirectoryLastWriteTime(path, lastWriteTime, out directoryExists);
        }

        public void ChangeMode(string path, ushort mode)
        {
        }

        public bool TryGetNormalizedPath(string path, out string normalizedPath, out string errorMessage)
        {
            return WindowsFileSystem.TryGetNormalizedPathImplementation(path, out normalizedPath, out errorMessage);
        }

        /// <summary>
        /// Hydrates a file by reading its first byte, triggering ProjFS placeholder hydration.
        /// </summary>
        /// <remarks>
        /// This was originally implemented using direct P/Invoke to kernel32 CreateFile/ReadFile
        /// for minimal overhead. During the .NET 10 NativeAOT migration, the P/Invoke path caused
        /// intermittent ACCESS_VIOLATION (0xC0000005) crashes under high concurrency in the
        /// HydrateFilesStage pipeline. The P/Invoke declarations also had incorrect parameter types
        /// (uint/int for pointer-sized params like LPSECURITY_ATTRIBUTES and LPOVERLAPPED).
        ///
        /// Replaced with managed FileStream, which internally calls the same Win32 APIs through the
        /// runtime's own NativeAOT-validated interop layer. Benchmarked at equivalent throughput
        /// (~36-40K files/s) in the multi-threaded scenario that matches actual HydrateFilesStage
        /// usage (ProcessorCount * 2 threads).
        /// </remarks>
        public bool HydrateFile(string fileName, byte[] buffer)
        {
            if (buffer.Length < 1)
            {
                throw new ArgumentException("Buffer must be at least 1 byte.", nameof(buffer));
            }

            try
            {
                using (FileStream fs = new FileStream(
                    fileName,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                {
                    // Read is intentionally inexact — we only need to trigger ProjFS hydration,
                    // not verify byte count. Empty files (0 bytes read) are fine.
#pragma warning disable CA2022
                    fs.Read(buffer, 0, 1);
#pragma warning restore CA2022
                }

                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        public bool IsExecutable(string fileName)
        {
            string fileExtension = Path.GetExtension(fileName);
            return string.Equals(fileExtension, ".exe", GVFSPlatform.Instance.Constants.PathComparison);
        }

        public bool IsSocket(string fileName)
        {
            return false;
        }

        /// <summary>
        /// Creates the specified directory (and its ancestors) if they do not
        /// already exist.
        ///
        /// If the specified directory does not exist this method:
        ///
        ///  - Creates the directory and its ancestors
        ///  - Adjusts the ACLs of 'directoryPath' (the ancestors' ACLs are not
        ///    modified).
        /// </summary>
        /// <returns>
        /// - true if the directory already exists -or- the directory was successfully created
        ///   with the proper ACLS
        /// - false otherwise
        /// </returns>
        /// <remarks>
        /// The following permissions are typically present on deskop and missing on Server.
        /// These are the permissions added by this method.
        ///
        ///   ACCESS_ALLOWED_ACE_TYPE: NT AUTHORITY\Authenticated Users
        ///          [OBJECT_INHERIT_ACE]
        ///          [CONTAINER_INHERIT_ACE]
        ///          [INHERIT_ONLY_ACE]
        ///        DELETE
        ///        GENERIC_EXECUTE
        ///        GENERIC_WRITE
        ///        GENERIC_READ
        /// </remarks>
        public bool TryCreateDirectoryAccessibleByAuthUsers(string directoryPath, out string error, ITracer tracer = null)
        {
            if (Directory.Exists(directoryPath))
            {
                error = null;
                return true;
            }

            try
            {
                // Create the directory first and then adjust the ACLs as needed
                Directory.CreateDirectory(directoryPath);

                // Use AccessRuleFactory rather than creating a FileSystemAccessRule because the NativeMethods.FileAccess flags
                // we're specifying are not valid for the FileSystemRights parameter of the FileSystemAccessRule constructor
                DirectoryInfo directoryInfo = new DirectoryInfo(directoryPath);
                DirectorySecurity directorySecurity = directoryInfo.GetAccessControl();
                AccessRule authenticatedUsersAccessRule = directorySecurity.AccessRuleFactory(
                    new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                    unchecked((int)(NativeMethods.FileAccess.DELETE | NativeMethods.FileAccess.GENERIC_EXECUTE | NativeMethods.FileAccess.GENERIC_WRITE | NativeMethods.FileAccess.GENERIC_READ)),
                    true,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow);

                // The return type of the AccessRuleFactory method is the base class, AccessRule, but the return value can be cast safely to the derived class.
                // https://msdn.microsoft.com/en-us/library/system.security.accesscontrol.filesystemsecurity.accessrulefactory(v=vs.110).aspx
                directorySecurity.AddAccessRule((FileSystemAccessRule)authenticatedUsersAccessRule);
                directoryInfo.SetAccessControl(directorySecurity);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is SystemException)
            {
                if (tracer != null)
                {
                    EventMetadata metadataData = new EventMetadata();
                    metadataData.Add("Exception", e.ToString());
                    metadataData.Add(nameof(directoryPath), directoryPath);
                    tracer.RelatedError(metadataData, $"{nameof(this.TryCreateDirectoryAccessibleByAuthUsers)}: Failed to create and configure directory");
                }

                error = e.Message;
                return false;
            }

            error = null;
            return true;
        }

        public bool TryCreateDirectoryWithAdminAndUserModifyPermissions(string directoryPath, out string error)
        {
            try
            {
                DirectorySecurity directorySecurity = new DirectorySecurity();

                // Protect the access rules from inheritance and remove any inherited rules
                directorySecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

                // Add new ACLs for users and admins.  Users will be granted write permissions.
                AddUsersAccessRulesToDirectorySecurity(directorySecurity, grantUsersModifyPermissions: true);
                AddAdminAccessRulesToDirectorySecurity(directorySecurity);

                directorySecurity.CreateDirectory(directoryPath);
            }
            catch (Exception e) when (e is IOException ||
                                      e is UnauthorizedAccessException ||
                                      e is PathTooLongException ||
                                      e is DirectoryNotFoundException)
            {
                error = $"Exception while creating directory `{directoryPath}`: {e.Message}";
                return false;
            }

            error = null;
            return true;
        }

        public bool TryCreateOrUpdateDirectoryToAdminModifyPermissions(ITracer tracer, string directoryPath, out string error)
        {
            try
            {
                DirectoryInfo directoryInfo = new DirectoryInfo(directoryPath);
                DirectorySecurity directorySecurity;
                if (Directory.Exists(directoryPath))
                {
                    directorySecurity = directoryInfo.GetAccessControl();
                }
                else
                {
                    directorySecurity = new DirectorySecurity();
                }

                // Protect the access rules from inheritance and remove any inherited rules
                directorySecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

                // Remove any existing ACLs and add new ACLs for users and admins
                RemoveAllFileSystemAccessRulesFromDirectorySecurity(directorySecurity);
                AddUsersAccessRulesToDirectorySecurity(directorySecurity, grantUsersModifyPermissions: false);
                AddAdminAccessRulesToDirectorySecurity(directorySecurity);

                directorySecurity.CreateDirectory(directoryPath);

                // Ensure the ACLs are set correctly if the directory already existed
                directoryInfo.SetAccessControl(directorySecurity);
            }
            catch (Exception e) when (e is IOException || e is SystemException)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Exception", e.ToString());
                tracer.RelatedError(metadata, $"{nameof(this.TryCreateOrUpdateDirectoryToAdminModifyPermissions)}: Exception while creating/configuring directory");

                error = e.Message;
                return false;
            }

            error = null;
            return true;
        }

        public bool IsFileSystemSupported(string path, out string error)
        {
            error = null;
            return true;
        }

        /// <summary>
        /// On Windows, if the current user is elevated, the owner of the directory will be the Administrators group by default.
        /// This runs afoul of the git "dubious ownership" check, which can fail if either the .git directory or the working directory
        /// are not owned by the current user.
        ///
        /// At the moment git for windows does not consider a non-elevated admin to be the owner of a directory owned by the Administrators group,
        /// though a fix is in progress in the microsoft fork of git. Libgit2(sharp) also does not have this fix.
        ///
        /// Also, even if the fix were in place, automount would still fail because it runs under SYSTEM account.
        ///
        /// This method ensures that the directory is owned by the current user (which is verified to work for SYSTEM account for automount).
        ///
        /// Exception: under Windows "Administrator protection" (AP), an elevated process runs as a profile-separated shadow admin
        /// account (MACHINE\admin_&lt;user&gt;) whose SID differs from the real (non-elevated) user's. In that case the directory owner is
        /// left as the Administrators group rather than being reassigned to the shadow admin (see the AP note inline).
        /// </summary>
        public void EnsureDirectoryIsOwnedByCurrentUser(string directoryPath)
        {
            // Ensure directory exists, inheriting all other ACLS
            Directory.CreateDirectory(directoryPath);
            // If the user is currently elevated, the owner of the directory will be the Administrators group.
            DirectoryInfo directoryInfo = new DirectoryInfo(directoryPath);
            DirectorySecurity directorySecurity = directoryInfo.GetAccessControl();
            IdentityReference directoryOwner = directorySecurity.GetOwner(typeof(SecurityIdentifier));
            SecurityIdentifier administratorsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            if (directoryOwner == administratorsSid)
            {
                // Under Windows "Administrator protection" (AP), an elevated process runs as a profile-separated shadow
                // admin account (MACHINE\admin_<user>) whose SID differs from the real (non-elevated) user's. Reassigning
                // ownership to the current user here would set the owner to that shadow admin, causing the real user to hit
                // "fatal: detected dubious ownership" — git's Administrators-membership grace does not cover another specific
                // user's SID. The shadow admin also cannot SetOwner to the real user's SID. Leaving the owner as the
                // Administrators group is accepted by modern git and the libgit2 non-elevated-admin-owner overlay for any
                // Administrators member (real user, shadow admin, and SYSTEM for automount alike), so skip the reassignment.
                //
                // Note: a libgit2 consumer that lacks the non-elevated-admin-owner patch (i.e. stock libgit2 without the
                // Administrators-membership fix) will not be able to open an Administrators-owned repo. Addressing that would
                // require setting git's safe.directory for the real user, but safe.directory is only honored from global/system
                // config (never repo-local), and an elevated clone runs as the shadow admin — so covering the real user would
                // mean mutating the real user's global gitconfig or machine-wide system config from the shadow admin. After
                // consideration, GVFS is not the right place to do that; unpatched third-party libgit2 consumers are out of scope.
                if (WindowsPlatform.IsCurrentUserAdminProtectionShadowAccount())
                {
                    return;
                }

                WindowsIdentity currentUser = WindowsIdentity.GetCurrent();
                directorySecurity.SetOwner(currentUser.User);
                directoryInfo.SetAccessControl(directorySecurity);
            }
        }
    }
}
