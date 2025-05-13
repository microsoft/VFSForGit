using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Pipes;

namespace GVFS.Common
{
    public abstract class GVFSPlatform
    {
        public GVFSPlatform(UnderConstructionFlags underConstruction)
        {
            this.UnderConstruction = underConstruction;
        }

        public static GVFSPlatform Instance { get; private set; }

        public abstract IKernelDriver KernelDriver { get; }
        public abstract IGitInstallation GitInstallation { get; }
        public abstract IDiskLayoutUpgradeData DiskLayoutUpgrade { get; }
        public abstract IPlatformFileSystem FileSystem { get; }

        public abstract GVFSPlatformConstants Constants { get; }
        public UnderConstructionFlags UnderConstruction { get; }
        public abstract string Name { get; }

        public abstract string GVFSConfigPath { get; }

        /// <summary>
        /// Returns true if the platform keeps a system-wide installer log.
        /// </summary>
        public abstract bool SupportsSystemInstallLog { get; }

        public static void Register(GVFSPlatform platform)
        {
            if (GVFSPlatform.Instance != null)
            {
                throw new InvalidOperationException("Cannot register more than one platform");
            }

            GVFSPlatform.Instance = platform;
        }

        /// <summary>
        /// Starts a VFS for Git process in the background.
        /// </summary>
        /// <remarks>
        /// This method should only be called by processes whose code we own as the background process must
        /// do some extra work after it starts.
        /// </remarks>
        public abstract void StartBackgroundVFS4GProcess(ITracer tracer, string programName, string[] args);

        /// <summary>
        /// Adjusts the current process for running in the background.
        /// </summary>
        /// <remarks>
        /// This method should be called after starting by processes launched using <see cref="GVFSPlatform.StartBackgroundVFS4GProcess"/>
        /// </remarks>
        /// <exception cref="Win32Exception">
        /// Failed to prepare process to run in background.
        /// </exception>
        public abstract void PrepareProcessToRunInBackground();

        public abstract bool IsProcessActive(int processId);
        public abstract void IsServiceInstalledAndRunning(string name, out bool installed, out bool running);
        public abstract string GetNamedPipeName(string enlistmentRoot);
        public abstract string GetGVFSServiceNamedPipeName(string serviceName);
        public abstract NamedPipeServerStream CreatePipeByName(string pipeName);

        public abstract string GetOSVersionInformation();
        public abstract string GetSecureDataRootForGVFS();
        public abstract string GetSecureDataRootForGVFSComponent(string componentName);
        public abstract string GetCommonAppDataRootForGVFS();
        public abstract string GetLogsDirectoryForGVFSComponent(string componentName);
        public abstract bool IsElevated();
        public abstract string GetCurrentUser();
        public abstract string GetUserIdFromLoginSessionId(int sessionId, ITracer tracer);
        public abstract string GetSystemInstallerLogPath();

        public abstract void ConfigureVisualStudio(string gitBinPath, ITracer tracer);

        public abstract bool TryCopyPanicLogs(string copyToDir, out string error);

        public abstract bool TryGetGVFSHooksVersion(out string hooksVersion, out string error);
        public abstract bool TryInstallGitCommandHooks(GVFSContext context, string executingDirectory, string hookName, string commandHookPath, out string errorMessage);

        public abstract Dictionary<string, string> GetPhysicalDiskInfo(string path, bool sizeStatsOnly);

        public abstract bool IsConsoleOutputRedirectedToFile();

        public abstract bool TryKillProcessTree(int processId, out int exitCode, out string error);

        public abstract bool TryGetGVFSEnlistmentRoot(string directory, out string enlistmentRoot, out string errorMessage);
        public abstract bool TryGetDefaultLocalCacheRoot(string enlistmentRoot, out string localCacheRoot, out string localCacheRootError);

        public abstract bool IsGitStatusCacheSupported();

        public abstract FileBasedLock CreateFileBasedLock(
            PhysicalFileSystem fileSystem,
            ITracer tracer,
            string lockPath);

        public bool TryGetNormalizedPathRoot(string path, out string pathRoot, out string errorMessage)
        {
            pathRoot = null;
            errorMessage = null;
            string normalizedPath = null;

            if (!this.FileSystem.TryGetNormalizedPath(path, out normalizedPath, out errorMessage))
            {
                return false;
            }

            pathRoot = Path.GetPathRoot(normalizedPath);
            return true;
        }

        public abstract class GVFSPlatformConstants
        {
            public static readonly char PathSeparator = Path.DirectorySeparatorChar;
            public abstract int MaxPipePathLength { get; }
            public abstract string ExecutableExtension { get; }
            public abstract string InstallerExtension { get; }

            /// <summary>
            /// Indicates whether the platform supports running the upgrade application while
            /// the upgrade verb is running.
            /// </summary>
            public abstract bool SupportsUpgradeWhileRunning { get; }
            public abstract string UpgradeInstallAdviceMessage { get; }
            public abstract string RunUpdateMessage { get; }
            public abstract string UpgradeConfirmCommandMessage { get; }
            public abstract string StartServiceCommandMessage { get; }
            public abstract string WorkingDirectoryBackingRootPath { get; }
            public abstract string DotGVFSRoot { get; }

            public abstract string GVFSBinDirectoryPath { get; }

            public abstract string GVFSBinDirectoryName { get; }

            public abstract string GVFSExecutableName { get; }


            /// <summary>
            /// Different platforms can have different requirements
            /// around which processes can block upgrade. For example,
            /// on Windows, we will block upgrade if any GVFS commands
            /// are running, but on POSIX platforms, we relax this
            /// constraint to allow upgrade to run while the upgrade
            /// command is running. Another example is that
            /// Non-windows platforms do not block upgrade when bash
            /// is running.
            /// </summary>
            public abstract HashSet<string> UpgradeBlockingProcesses { get; }

            public abstract bool CaseSensitiveFileSystem { get; }

            public StringComparison PathComparison
            {
                get
                {
                    return this.CaseSensitiveFileSystem ?
                        StringComparison.Ordinal :
                        StringComparison.OrdinalIgnoreCase;
                }
            }

            public StringComparer PathComparer
            {
                get
                {
                    return this.CaseSensitiveFileSystem ?
                        StringComparer.Ordinal :
                        StringComparer.OrdinalIgnoreCase;
                }
            }

            public string GVFSHooksExecutableName
            {
                get { return "GVFS.Hooks" + this.ExecutableExtension; }
            }

            public string GVFSReadObjectHookExecutableName
            {
                get { return "GVFS.ReadObjectHook" + this.ExecutableExtension; }
            }

            public string GVFSVirtualFileSystemHookExecutableName
            {
                get { return "GVFS.VirtualFileSystemHook" + this.ExecutableExtension; }
            }

            public string GVFSPostIndexChangedHookExecutableName
            {
                get { return "GVFS.PostIndexChangedHook" + this.ExecutableExtension; }
            }

            public string MountExecutableName
            {
                get { return "GVFS.Mount" + this.ExecutableExtension; }
            }
        }

        public class UnderConstructionFlags
        {
            public UnderConstructionFlags(
                bool supportsGVFSUpgrade = true,
                bool supportsGVFSConfig = true,
                bool supportsNuGetEncryption = true,
                bool supportsNuGetVerification = true)
            {
                this.SupportsGVFSUpgrade = supportsGVFSUpgrade;
                this.SupportsGVFSConfig = supportsGVFSConfig;
                this.SupportsNuGetEncryption = supportsNuGetEncryption;
                this.SupportsNuGetVerification = supportsNuGetVerification;
            }

            public bool SupportsGVFSUpgrade { get; }
            public bool SupportsGVFSConfig { get; }
            public bool SupportsNuGetEncryption { get; }
            public bool SupportsNuGetVerification { get; }
        }
    }
}
