using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using GVFS.UnitTests.Mock.Common.Tracing;
using GVFS.UnitTests.Mock.FileSystem;
using System;
using System.Collections.Generic;
using System.IO.Pipes;

namespace GVFS.UnitTests.Mock.Common
{
    public class MockPlatform : GVFSPlatform
    {
        public MockPlatform()
            : base(executableExtension: ".mockexe")
        {
        }

        public override IKernelDriver KernelDriver => throw new NotSupportedException();

        public override IGitInstallation GitInstallation => throw new NotSupportedException();

        public override IDiskLayoutUpgradeData DiskLayoutUpgrade => throw new NotSupportedException();

        public override IPlatformFileSystem FileSystem { get; } = new MockPlatformFileSystem();

        public override void ConfigureVisualStudio(string gitBinPath, ITracer tracer)
        {
            throw new NotSupportedException();
        }

        public override bool TryGetGVFSHooksPathAndVersion(out string hooksPaths, out string hooksVersion, out string error)
        {
            throw new NotSupportedException();
        }

        public override bool TryInstallGitCommandHooks(GVFSContext context, string executingDirectory, string hookName, string commandHookPath, out string errorMessage)
        {
            throw new NotSupportedException();
        }

        public override string GetNamedPipeName(string enlistmentRoot)
        {
            return "GVFS_Mock_PipeName";
        }

        public override NamedPipeServerStream CreatePipeByName(string pipeName)
        {
            throw new NotSupportedException();
        }

        public override InProcEventListener CreateTelemetryListenerIfEnabled(string providerName)
        {
            return new MockListener(EventLevel.Verbose, Keywords.Telemetry);
        }

        public override string GetCurrentUser()
        {
            throw new NotSupportedException();
        }

        public override string GetOSVersionInformation()
        {
            throw new NotSupportedException();
        }

        public override Dictionary<string, string> GetPhysicalDiskInfo(string path)
        {
            throw new NotSupportedException();
        }

        public override void InitializeEnlistmentACLs(string enlistmentPath)
        {
            throw new NotSupportedException();
        }

        public override bool IsConsoleOutputRedirectedToFile()
        {
            throw new NotSupportedException();
        }

        public override bool IsElevated()
        {
            throw new NotSupportedException();
        }

        public override bool IsProcessActive(int processId)
        {
            throw new NotSupportedException();
        }

        public override bool TryGetGVFSEnlistmentRoot(string directory, out string enlistmentRoot, out string errorMessage)
        {
            throw new NotSupportedException();
        }

        public override void StartBackgroundProcess(string programName, string[] args)
        {
            throw new NotSupportedException();
        }

        public override bool IsGitStatusCacheSupported()
        {
            return true;
        }

        public override IFileBasedLock CreateFileBasedLock(PhysicalFileSystem fileSystem, ITracer tracer, string lockPath, string signature)
        {
            return new MockFileBasedLock();
        }
    }
}
