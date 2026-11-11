using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace GVFS.Platform.Windows
{
    public class InstallationTelemetrySecurity
    {
        private readonly SecurityIdentifier currentUser;

        public InstallationTelemetrySecurity()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                this.currentUser = identity.User;
            }
        }

        public virtual void VerifyDirectory(string path)
        {
            if (path.StartsWith(@"\\", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Installation telemetry requires a local directory.");
            }

            DirectoryInfo directory = new DirectoryInfo(path);
            this.VerifyPath(directory, isDirectory: true, ancestorsOnly: false);
            for (DirectoryInfo parent = directory.Parent; parent != null; parent = parent.Parent)
            {
                this.VerifyPath(parent, isDirectory: true, ancestorsOnly: true);
            }
        }

        public virtual void VerifyFile(string path)
        {
            this.VerifyPath(new FileInfo(path), isDirectory: false, ancestorsOnly: false);
        }

        public void VerifyDescriptor(RawSecurityDescriptor security, bool ancestorsOnly)
        {
            if (!this.IsTrusted(security.Owner) || security.DiscretionaryAcl == null || security.DiscretionaryAcl.Count == 0)
            {
                throw new UnauthorizedAccessException("Installation telemetry requires a trusted owner and access rules.");
            }

            uint unsafeRights = ancestorsOnly ? 0x100D0040u : 0x500D0156u;
            foreach (GenericAce ace in security.DiscretionaryAcl)
            {
                if ((ace.AceFlags & AceFlags.InheritOnly) != 0)
                {
                    continue;
                }

                if (ace is not CommonAce common || (common.AceType != AceType.AccessAllowed && common.AceType != AceType.AccessDenied))
                {
                    throw new UnauthorizedAccessException("Installation telemetry path has unsupported access rules.");
                }

                if (common.AceType == AceType.AccessAllowed && !this.IsTrusted(common.SecurityIdentifier)
                    && (unchecked((uint)common.AccessMask) & unsafeRights) != 0)
                {
                    throw new UnauthorizedAccessException("Installation telemetry path permits untrusted modification.");
                }
            }
        }

        private bool IsTrusted(SecurityIdentifier sid)
        {
            return sid != null && (sid == this.currentUser
                || sid.IsWellKnown(WellKnownSidType.LocalSystemSid)
                || sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid)
                || sid.Value == "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464");
        }

        private void VerifyPath(FileSystemInfo entry, bool isDirectory, bool ancestorsOnly)
        {
            FileAttributes attributes = entry.Attributes;
            if ((attributes & FileAttributes.ReparsePoint) != 0
                || ((attributes & FileAttributes.Directory) != 0) != isDirectory)
            {
                throw new InvalidDataException("Installation telemetry path has invalid attributes: " + entry.FullName);
            }

            FileSystemSecurity security = isDirectory
                ? (FileSystemSecurity)new DirectoryInfo(entry.FullName).GetAccessControl()
                : new FileInfo(entry.FullName).GetAccessControl();
            this.VerifyDescriptor(new RawSecurityDescriptor(security.GetSecurityDescriptorBinaryForm(), 0), ancestorsOnly);
        }
    }
}
