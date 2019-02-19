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
        public GVFSPlatform(string executableExtension, string installerExtension, UnderConstructionFlags underConstruction, DiskLayoutVersion diskLayoutVersion)
        {
            this.Constants = new GVFSPlatformConstants(executableExtension, installerExtension);
            this.UnderConstruction = underConstruction;
            this.DiskLayout = diskLayoutVersion;
        }

        public static GVFSPlatform Instance { get; private set; }

        public abstract IKernelDriver KernelDriver { get; }
        public abstract IGitInstallation GitInstallation { get; }
        public abstract IDiskLayoutUpgradeData DiskLayoutUpgrade { get; }
        public abstract IPlatformFileSystem FileSystem { get; }

        public GVFSPlatformConstants Constants { get; }
        public UnderConstructionFlags UnderConstruction { get; }
        public DiskLayoutVersion DiskLayout { get; }

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
        public abstract NamedPipeServerStream CreatePipeByName(string pipeName);

        public abstract string GetOSVersionInformation();
        public abstract void InitializeEnlistmentACLs(string enlistmentPath);
        public abstract bool IsElevated();
        public abstract string GetCurrentUser();
        public abstract void ConfigureVisualStudio(string gitBinPath, ITracer tracer);

        public abstract bool TryGetGVFSHooksPathAndVersion(out string hooksPaths, out string hooksVersion, out string error);
        public abstract bool TryInstallGitCommandHooks(GVFSContext context, string executingDirectory, string hookName, string commandHookPath, out string errorMessage);

        public abstract IEnumerable<EventListener> CreateTelemetryListeners(string providerName, string enlistmentId, string mountId);

        public abstract bool TryVerifyAuthenticodeSignature(string path, out string subject, out string issuer, out string error);

        public abstract Dictionary<string, string> GetPhysicalDiskInfo(string path, bool sizeStatsOnly);

        public abstract bool IsConsoleOutputRedirectedToFile();
        public abstract bool TryGetGVFSEnlistmentRoot(string directory, out string enlistmentRoot, out string errorMessage);

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

        public class GVFSPlatformConstants
        {
            public static readonly char PathSeparator = Path.DirectorySeparatorChar;

            public GVFSPlatformConstants(string executableExtension, string installerExtension)
            {
                this.ExecutableExtension = executableExtension;
                this.InstallerExtension = installerExtension;
            }

            public string ExecutableExtension { get; }
            public string InstallerExtension { get; }

            public string GVFSExecutableName
            {
                get { return "GVFS" + this.ExecutableExtension; }
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
                get { return "GVFS.Upgrader" + this.ExecutableExtension;  }
            }
        }

        public class UnderConstructionFlags
        {
            public UnderConstructionFlags(
                bool supportsGVFSService = true,
                bool supportsGVFSUpgrade = true,
                bool supportsGVFSConfig = true,
                bool requiresDeprecatedGitHooksLoader = false)
            {
                this.SupportsGVFSService = supportsGVFSService;
                this.SupportsGVFSUpgrade = supportsGVFSUpgrade;
                this.SupportsGVFSConfig = supportsGVFSConfig;
                this.RequiresDeprecatedGitHooksLoader = requiresDeprecatedGitHooksLoader;
            }

            public bool SupportsGVFSService { get; }
            public bool SupportsGVFSUpgrade { get; }
            public bool SupportsGVFSConfig { get; }
            public bool RequiresDeprecatedGitHooksLoader { get; }
        }

        public class DiskLayoutVersion
        {
            public DiskLayoutVersion(int currentMajorVersion, int currentMinorVersion, int minimumSupportedMajorVersion)
            {
                this.CurrentMajorVersion = currentMajorVersion;
                this.CurrentMinorVersion = currentMinorVersion;
                this.MinimumSupportedMajorVersion = minimumSupportedMajorVersion;
            }

            // The major version should be bumped whenever there is an on-disk format change that requires a one-way upgrade.
            // Increasing this version will make older versions of GVFS unable to mount a repo that has been mounted by a newer
            // version of GVFS.
            public int CurrentMajorVersion { get; }

            // The minor version should be bumped whenever there is an upgrade that can be safely ignored by older versions of GVFS.
            // For example, this allows an upgrade step that sets a default value for some new config setting.
            public int CurrentMinorVersion { get; }

            // This is the last time GVFS made a breaking change that required a reclone. This should not
            // be incremented on platforms that have released a v1.0 as all their format changes should be
            // supported with an upgrade step.
            public int MinimumSupportedMajorVersion { get; }
        }
    }
}
