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
        public static GVFSPlatform Instance { get; private set; }

        public abstract IKernelDriver KernelDriver { get; }
        public abstract IGitInstallation GitInstallation { get; }
        public abstract IDiskLayoutUpgradeData DiskLayoutUpgrade { get; }
        public abstract IPlatformFileSystem FileSystem { get; }
        public virtual bool IsUnderConstruction { get; } = false;
        public virtual bool SupportsGVFSService { get; } = true;
        public static void Register(GVFSPlatform platform)
        {
            if (GVFSPlatform.Instance != null)
            {
                throw new InvalidOperationException("Cannot register more than one platform");
            }

            GVFSPlatform.Instance = platform;
        }

        public abstract void StartBackgroundProcess(string programName, string[] args);
        public abstract NamedPipeServerStream CreatePipeByName(string pipeName);
        public abstract string GetOSVersionInformation();
        public abstract void InitializeEnlistmentACLs(string enlistmentPath);
        public abstract bool IsElevated();
        public abstract string GetCurrentUser();
        public abstract void ConfigureVisualStudio(string gitBinPath, ITracer tracer);
        public abstract bool TryGetGVFSHooksPathAndVersion(out string hooksPaths, out string hooksVersion, out string error);        

        public abstract InProcEventListener CreateTelemetryListenerIfEnabled(string providerName);

        public abstract Dictionary<string, string> GetPhysicalDiskInfo(string path);

        public abstract bool IsConsoleOutputRedirectedToFile();
        public abstract bool TryGetGVFSEnlistmentRoot(string directory, out string enlistmentRoot, out string errorMessage);

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
    }
}
