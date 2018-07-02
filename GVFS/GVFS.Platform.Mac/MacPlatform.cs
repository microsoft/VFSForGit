using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO.Pipes;

namespace GVFS.Platform.Mac
{
    public class MacPlatform : GVFSPlatform
    {
        public override IKernelDriver KernelDriver { get; } = new ProjFSKext();
        public override IGitInstallation GitInstallation { get; } = new MacGitInstallation();
        public override IDiskLayoutUpgradeData DiskLayoutUpgrade { get; } = new MacDiskLayoutUpgradeData();
        public override IPlatformFileSystem FileSystem { get; } = new MacFileSystem();
        public override bool IsUnderConstruction { get; } = true;
        public override bool SupportsGVFSService { get; } = false;

        public override void ConfigureVisualStudio(string gitBinPath, ITracer tracer)
        {
        }

        public override bool TryGetGVFSHooksPathAndVersion(out string hooksPaths, out string hooksVersion, out string error)
        {
            hooksPaths = string.Empty;

            // TODO(Mac): Get the hooks version rather than the GVFS version (and share that code with the Windows platform)
            hooksVersion = ProcessHelper.GetCurrentProcessVersion();
            error = null;
            return true;
        }

        public override void StartBackgroundProcess(string programName, string[] args)
        {
            ProcessLauncher.StartBackgroundProcess(programName, args);
        }

        public override NamedPipeServerStream CreatePipeByName(string pipeName)
        {
            NamedPipeServerStream pipe = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.WriteThrough | PipeOptions.Asynchronous,
                0,  // default inBufferSize
                0); // default outBufferSize)

            return pipe;
        }

        public override InProcEventListener CreateTelemetryListenerIfEnabled(string providerName)
        {
            return null;
        }

        public override string GetCurrentUser()
        {
            throw new NotImplementedException();
        }

        public override string GetOSVersionInformation()
        {
            throw new NotImplementedException();
        }

        public override Dictionary<string, string> GetPhysicalDiskInfo(string path)
        {
            // TODO(Mac): Collect disk information
            Dictionary<string, string> result = new Dictionary<string, string>();
            result.Add("GetPhysicalDiskInfo", "Not yet implemented on Mac");
            return result;
        }

        public override void InitializeEnlistmentACLs(string enlistmentPath)
        {
        }

        public override bool IsConsoleOutputRedirectedToFile()
        {
            // TODO(Mac): Implement proper check
            return false;
        }

        public override bool IsElevated()
        {
            // TODO(Mac): Implement proper check
            return false;
        }

        public override bool TryGetGVFSEnlistmentRoot(string directory, out string enlistmentRoot, out string errorMessage)
        {
            // TODO(Mac): Merge this code with the implementation in WindowsPlatform

            enlistmentRoot = null;

            string finalDirectory;
            if (!this.FileSystem.TryGetNormalizedPath(directory, out finalDirectory, out errorMessage))
            {
                return false;
            }

            enlistmentRoot = Paths.GetRoot(finalDirectory, GVFSConstants.DotGVFS.Root);
            if (enlistmentRoot == null)
            {
                errorMessage = $"Failed to find the root directory for {GVFSConstants.DotGVFS.Root} in {finalDirectory}";
                return false;
            }

            return true;
        }
    }
}
