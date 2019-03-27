using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;

namespace GVFS.Platform.Linux
{
    public partial class LinuxPlatform : GVFSPlatform
    {
        public const string StorageConfigName = "vfsforgit.conf";

        private string storageConfigPath;

        public LinuxPlatform()
            : base(
                executableExtension: string.Empty,
                installerExtension: string.Empty,
                underConstruction: new UnderConstructionFlags(
                    supportsGVFSService: false,
                    supportsGVFSUpgrade: false,
                    supportsGVFSConfig: false))
        {
        }

        public override IKernelDriver KernelDriver { get; } = new ProjFSLib();
        public override IGitInstallation GitInstallation { get; } = new LinuxGitInstallation();
        public override IDiskLayoutUpgradeData DiskLayoutUpgrade { get; } = new LinuxDiskLayoutUpgradeData();
        public override IPlatformFileSystem FileSystem { get; } = new LinuxFileSystem();

        public override void ConfigureVisualStudio(string gitBinPath, ITracer tracer)
        {
        }

        public override bool TryGetGVFSHooksPathAndVersion(out string hooksPaths, out string hooksVersion, out string error)
        {
            hooksPaths = string.Empty;

            // TODO(Linux): Get the hooks version rather than the GVFS version (and share that code with the Windows platform)
            hooksVersion = ProcessHelper.GetCurrentProcessVersion();
            error = null;
            return true;
        }

        public override bool TryInstallGitCommandHooks(GVFSContext context, string executingDirectory, string hookName, string commandHookPath, out string errorMessage)
        {
            errorMessage = null;

            string gvfsHooksPath = Path.Combine(executingDirectory, GVFSPlatform.Instance.Constants.GVFSHooksExecutableName);

            File.WriteAllText(
                commandHookPath,
                $"#!/bin/sh\n{gvfsHooksPath} {hookName} \"$@\"");
            GVFSPlatform.Instance.FileSystem.ChangeMode(commandHookPath, Convert.ToUInt16("755", 8));

            return true;
        }

        public override bool TryVerifyAuthenticodeSignature(string path, out string subject, out string issuer, out string error)
        {
            throw new NotImplementedException();
        }

        public override bool IsProcessActive(int processId)
        {
            return LinuxPlatform.IsProcessActiveImplementation(processId);
        }

        public override void IsServiceInstalledAndRunning(string name, out bool installed, out bool running)
        {
            throw new NotImplementedException();
        }

        public override void StartBackgroundProcess(ITracer tracer, string programName, string[] args)
        {
            ProcessLauncher.StartBackgroundProcess(tracer, programName, args);
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

        public override IEnumerable<EventListener> CreateTelemetryListeners(string providerName, string enlistmentId, string mountId)
        {
            // TODO: return TelemetryDaemonEventListener when the telemetry daemon has been implemented for Linux

            // string gitBinRoot = this.GitInstallation.GetInstalledGitBinPath();
            // var daemonListener = TelemetryDaemonEventListener.CreateIfEnabled(gitBinRoot, providerName, enlistmentId, mountId, pipeName: "vfs");
            // if (daemonListener != null)
            // {
            //     yield return daemonListener;
            // }

            yield break;
        }

        public override string GetCurrentUser()
        {
            throw new NotImplementedException();
        }

        public override string GetOSVersionInformation()
        {
            ProcessResult result = ProcessHelper.Run("uname", args: "-srv", redirectOutput: true);
            return string.IsNullOrWhiteSpace(result.Output) ? result.Errors : result.Output;
        }

        public override Dictionary<string, string> GetPhysicalDiskInfo(string path, bool sizeStatsOnly)
        {
            // TODO(Linux): Collect disk information
            Dictionary<string, string> result = new Dictionary<string, string>();
            result.Add("GetPhysicalDiskInfo", "Not yet implemented on Linux");
            return result;
        }

        public override void InitializeEnlistmentACLs(string enlistmentPath)
        {
        }

        public override void InitializeStorageMapping(string dotGVFSRoot, string workingDirectoryRoot)
        {
            this.storageConfigPath = Path.Combine(workingDirectoryRoot, StorageConfigName);

            FileInfo storageConfig = new FileInfo(this.storageConfigPath);
            using (FileStream fs = storageConfig.Create())
            using (StreamWriter writer = new StreamWriter(fs))
            {
                writer.Write(
$@"# This source directory was created by the VFSForGit 'clone' command,
# and its contents will be projected (virtualized) by running the
# VFSForGit 'mount' command.
#
# Do not be alarmed if the directory appears empty except for this file!
# Running the VFSForGit 'mount' command in the parent directory will
# cause this directory to be mounted and the contents projected into place.

# Lower storage directory; used by projfs when virtualizing.
lowerdir={dotGVFSRoot}
# Initial mount flag; will be set to 'false' after first mount.
initial=true
");
            }
        }

        public override string GetNamedPipeName(string enlistmentRoot)
        {
            return LinuxPlatform.GetNamedPipeNameImplementation(enlistmentRoot);
        }

        public override bool IsConsoleOutputRedirectedToFile()
        {
            return LinuxPlatform.IsConsoleOutputRedirectedToFileImplementation();
        }

        public override bool IsElevated()
        {
            return LinuxPlatform.IsElevatedImplementation();
        }

        public override bool TryGetGVFSEnlistmentRoot(string directory, out string enlistmentRoot, out string errorMessage)
        {
            return LinuxPlatform.TryGetGVFSEnlistmentRootImplementation(directory, out enlistmentRoot, out errorMessage);
        }

        public override bool IsGitStatusCacheSupported()
        {
            // TODO(Linux): support git status cache
            return false;
        }

        public override FileBasedLock CreateFileBasedLock(
            PhysicalFileSystem fileSystem,
            ITracer tracer,
            string lockPath)
        {
            return new LinuxFileBasedLock(fileSystem, tracer, lockPath);
        }

        public override bool TryKillProcessTree(int processId, out int exitCode, out string error)
        {
            ProcessResult result = ProcessHelper.Run("pkill", $"-P {processId}");
            error = result.Errors;
            exitCode = result.ExitCode;
            return result.ExitCode == 0;
        }
    }
}
