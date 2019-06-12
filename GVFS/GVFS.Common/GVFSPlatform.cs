using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
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

        public static void Register(GVFSPlatform platform)
        {
            if (GVFSPlatform.Instance != null)
            {
                throw new InvalidOperationException("Cannot register more than one platform");
            }

            GVFSPlatform.Instance = platform;
        }

        public abstract void StartBackgroundProcess(ITracer tracer, string programName, string[] args);
        public abstract bool IsProcessActive(int processId);
        public abstract void IsServiceInstalledAndRunning(string name, out bool installed, out bool running);
        public abstract string GetNamedPipeName(string enlistmentRoot);
        public abstract string GetGVFSServiceNamedPipeName(string serviceName);
        public abstract NamedPipeServerStream CreatePipeByName(string pipeName);

        public abstract string GetOSVersionInformation();
        public abstract string GetDataRootForGVFS();
        public abstract string GetDataRootForGVFSComponent(string componentName);
        public abstract void InitializeEnlistmentACLs(string enlistmentPath);
        public abstract bool IsElevated();
        public abstract string GetCurrentUser();
        public abstract string GetUserIdFromLoginSessionId(int sessionId, ITracer tracer);
        public abstract void ConfigureVisualStudio(string gitBinPath, ITracer tracer);

        public abstract bool TryGetGVFSHooksPathAndVersion(out string hooksPaths, out string hooksVersion, out string error);
        public abstract bool TryInstallGitCommandHooks(GVFSContext context, string executingDirectory, string hookName, string commandHookPath, out string errorMessage);

        public abstract bool TryVerifyAuthenticodeSignature(string path, out string subject, out string issuer, out string error);

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
            public abstract string ExecutableExtension { get; }
            public abstract string InstallerExtension { get; }
            public abstract string WorkingDirectoryBackingRootPath { get; }
            public abstract string DotGVFSRoot { get; }

            public abstract string GVFSBinDirectoryPath { get; }

            public abstract string GVFSBinDirectoryName { get; }

            public abstract string GVFSExecutableName { get; }

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

            public string GVFSUpgraderExecutableName
            {
                get { return "GVFS.Upgrader" + this.ExecutableExtension; }
            }
        }

        public class UnderConstructionFlags
        {
            public UnderConstructionFlags(
                bool supportsGVFSUpgrade = true,
                bool supportsGVFSConfig = true,
                bool requiresDeprecatedGitHooksLoader = false)
            {
                this.SupportsGVFSUpgrade = supportsGVFSUpgrade;
                this.SupportsGVFSConfig = supportsGVFSConfig;
                this.RequiresDeprecatedGitHooksLoader = requiresDeprecatedGitHooksLoader;
            }

            public bool SupportsGVFSUpgrade { get; }
            public bool SupportsGVFSConfig { get; }
            public bool RequiresDeprecatedGitHooksLoader { get; }
        }
    }
}
