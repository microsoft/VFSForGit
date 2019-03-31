using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Security;

namespace GVFS.Platform.Linux
{
    public partial class LinuxPlatform : GVFSPlatform
    {
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

        public override bool TryGetDefaultLocalCacheRoot(string enlistmentRoot, out string localCacheRoot, out string localCacheRootError)
        {
            string homeDirectory;

            try
            {
                homeDirectory = Environment.GetEnvironmentVariable("HOME");
            }
            catch (SecurityException e)
            {
                localCacheRoot = null;
                localCacheRootError = $"Failed to read $HOME, insufficient permission: {e.Message}";
                return false;
            }

            if (string.IsNullOrEmpty(homeDirectory))
            {
                localCacheRoot = null;
                localCacheRootError = "$HOME empty or not found";
                return false;
            }

            try
            {
                localCacheRoot = Path.Combine(homeDirectory, GVFSConstants.DefaultGVFSCacheFolderName);
                localCacheRootError = null;
                return true;
            }
            catch (ArgumentException e)
            {
                localCacheRoot = null;
                localCacheRootError = $"Failed to build local cache path using $HOME('{homeDirectory}'): {e.Message}";
                return false;
            }
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
