using GVFS.Common.Git;
using GVFS.Common.NamedPipes;
using Newtonsoft.Json;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;

namespace GVFS.Common
{
    public class GVFSEnlistment : Enlistment
    {
        public const string InvalidRepoUrl = "invalid://repoUrl";

        // New enlistment
        public GVFSEnlistment(string enlistmentRoot, string repoUrl, string gitBinPath, string gvfsHooksRoot)
            : base(
                  enlistmentRoot, 
                  Path.Combine(enlistmentRoot, GVFSConstants.WorkingDirectoryRootName),
                  Path.Combine(enlistmentRoot, GVFSConstants.DotGVFS.GitObjectCachePath),
                  repoUrl,
                  gitBinPath, 
                  gvfsHooksRoot)
        {
            this.NamedPipeName = Paths.GetNamedPipeName(this.EnlistmentRoot);
            this.DotGVFSRoot = Path.Combine(this.EnlistmentRoot, GVFSConstants.DotGVFS.Root);
            this.GVFSLogsRoot = Path.Combine(this.EnlistmentRoot, GVFSConstants.DotGVFS.LogPath);
        }
        
        // Existing, configured enlistment
        public GVFSEnlistment(string enlistmentRoot, string gitBinPath, string gvfsHooksRoot)
            : this(
                  enlistmentRoot,
                  null,
                  gitBinPath,
                  gvfsHooksRoot)
        {
        }

        public string NamedPipeName { get; private set; }

        public string DotGVFSRoot { get; private set; }

        public string GVFSLogsRoot { get; private set; }

        public static GVFSEnlistment CreateWithoutRepoUrlFromDirectory(string directory, string gitBinRoot, string gvfsHooksRoot)
        {
            if (Directory.Exists(directory))
            {
                string enlistmentRoot = Paths.GetGVFSEnlistmentRoot(directory);
                if (enlistmentRoot != null)
                {
                    return new GVFSEnlistment(enlistmentRoot, InvalidRepoUrl, gitBinRoot, gvfsHooksRoot);
                }
            }

            return null;
        }

        public static GVFSEnlistment CreateFromDirectory(string directory, string gitBinRoot, string gvfsHooksRoot)
        {
            if (Directory.Exists(directory))
            {
                string enlistmentRoot = Paths.GetGVFSEnlistmentRoot(directory);
                if (enlistmentRoot != null)
                {
                    return new GVFSEnlistment(enlistmentRoot, gitBinRoot, gvfsHooksRoot);
                }
            }

            return null;
        }

        public static string GetNewGVFSLogFileName(string logsRoot, string logFileType)
        {
            return Enlistment.GetNewLogFileName(
                logsRoot, 
                "gvfs_" + logFileType);
        }
        
        public static bool WaitUntilMounted(string enlistmentRoot, out string errorMessage)
        {
            errorMessage = null;
            using (NamedPipeClient pipeClient = new NamedPipeClient(NamedPipeClient.GetPipeNameFromPath(enlistmentRoot)))
            {
                if (!pipeClient.Connect(GVFSConstants.NamedPipes.ConnectTimeoutMS))
                {
                    errorMessage = "Unable to mount because the GVFS.Mount process is not responding.";
                    return false;
                }

                while (true)
                {
                    string response = string.Empty;
                    try
                    {
                        pipeClient.SendRequest(NamedPipeMessages.GetStatus.Request);
                        response = pipeClient.ReadRawResponse();
                        NamedPipeMessages.GetStatus.Response getStatusResponse =
                            NamedPipeMessages.GetStatus.Response.FromJson(response);

                        if (getStatusResponse.MountStatus == NamedPipeMessages.GetStatus.Ready)
                        {
                            return true;
                        }
                        else if (getStatusResponse.MountStatus == NamedPipeMessages.GetStatus.MountFailed)
                        {
                            errorMessage = string.Format("Failed to mount at {0}", enlistmentRoot);
                            return false;
                        }
                        else
                        {
                            Thread.Sleep(500);
                        }
                    }
                    catch (BrokenPipeException e)
                    {
                        errorMessage = string.Format("Could not connect to GVFS.Mount: {0}", e);
                        return false;
                    }
                    catch (JsonReaderException e)
                    {
                        errorMessage = string.Format("Failed to parse response from GVFS.Mount.\n {0}", e);
                        return false;
                    }
                }
            }
        }
        
        public bool TryCreateEnlistmentFolders()
        {
            try
            {
                Directory.CreateDirectory(this.EnlistmentRoot);

                // The following permissions are typically present on deskop and missing on Server
                //                  
                //   ACCESS_ALLOWED_ACE_TYPE: NT AUTHORITY\Authenticated Users
                //          [OBJECT_INHERIT_ACE]
                //          [CONTAINER_INHERIT_ACE]
                //          [INHERIT_ONLY_ACE]
                //        DELETE
                //        GENERIC_EXECUTE
                //        GENERIC_WRITE
                //        GENERIC_READ
                DirectorySecurity rootSecurity = Directory.GetAccessControl(this.EnlistmentRoot);
                AccessRule authenticatedUsersAccessRule = rootSecurity.AccessRuleFactory(
                    new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                    unchecked((int)(NativeMethods.FileAccess.DELETE | NativeMethods.FileAccess.GENERIC_EXECUTE | NativeMethods.FileAccess.GENERIC_WRITE | NativeMethods.FileAccess.GENERIC_READ)),
                    true,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow);

                // The return type of the AccessRuleFactory method is the base class, AccessRule, but the return value can be cast safely to the derived class.
                // https://msdn.microsoft.com/en-us/library/system.security.accesscontrol.filesystemsecurity.accessrulefactory(v=vs.110).aspx
                rootSecurity.AddAccessRule((FileSystemAccessRule)authenticatedUsersAccessRule);
                Directory.SetAccessControl(this.EnlistmentRoot, rootSecurity);

                Directory.CreateDirectory(this.WorkingDirectoryRoot);
                this.CreateHiddenDirectory(this.DotGVFSRoot);
            }
            catch (IOException)
            {
                return false;
            }

            return true;
        }

        public bool TryConfigureAlternate(out string errorMessage)
        {
            try
            {
                if (!Directory.Exists(this.GitObjectsRoot))
                {
                    Directory.CreateDirectory(this.GitObjectsRoot);
                    Directory.CreateDirectory(this.GitPackRoot);
                }

                File.WriteAllText(
                    Path.Combine(this.WorkingDirectoryRoot, GVFSConstants.DotGit.Objects.Info.Alternates),
                    @"..\..\..\" + GVFSConstants.DotGVFS.GitObjectCachePath);
            }
            catch (IOException e)
            {
                errorMessage = e.Message;
                return false;
            }

            errorMessage = null;
            return true;
        }
        
        /// <summary>
        /// Creates a hidden directory @ the given path.
        /// If directory already exists, hides it.
        /// </summary>
        /// <param name="path">Path to desired hidden directory</param>
        private void CreateHiddenDirectory(string path)
        {
            DirectoryInfo dir = Directory.CreateDirectory(path);
            dir.Attributes = FileAttributes.Hidden;
        }
    }
}
