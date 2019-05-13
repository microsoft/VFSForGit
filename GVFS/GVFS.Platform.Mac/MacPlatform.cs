using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using GVFS.Platform.POSIX;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace GVFS.Platform.Mac
{
    public partial class MacPlatform : POSIXPlatform
    {
        private const string UpgradeProtectedDataDirectory = "/usr/local/vfsforgit_upgrader";

        public MacPlatform() : base(
             underConstruction: new UnderConstructionFlags(
                supportsGVFSUpgrade: true,
                supportsGVFSConfig: true,
                supportsNuGetEncryption: false))
        {
        }

        public override IDiskLayoutUpgradeData DiskLayoutUpgrade { get; } = new MacDiskLayoutUpgradeData();
        public override IKernelDriver KernelDriver { get; } = new ProjFSKext();
        public override string Name { get => "macOS"; }
        public override GVFSPlatformConstants Constants { get; } = new MacPlatformConstants();
        public override IPlatformFileSystem FileSystem { get; } = new MacFileSystem();

        public override string GVFSConfigPath
        {
            get
            {
                return Path.Combine(this.Constants.GVFSBinDirectoryPath, LocalGVFSConfig.FileName);
            }
        }

        public override string GetOSVersionInformation()
        {
            ProcessResult result = ProcessHelper.Run("sw_vers", args: string.Empty, redirectOutput: true);
            return string.IsNullOrWhiteSpace(result.Output) ? result.Errors : result.Output;
        }

        public override string GetDataRootForGVFS()
        {
            return MacPlatform.GetDataRootForGVFSImplementation();
        }

        public override string GetDataRootForGVFSComponent(string componentName)
        {
            return MacPlatform.GetDataRootForGVFSComponentImplementation(componentName);
        }

        public override bool TryGetGVFSEnlistmentRoot(string directory, out string enlistmentRoot, out string errorMessage)
        {
            return MacPlatform.TryGetGVFSEnlistmentRootImplementation(directory, out enlistmentRoot, out errorMessage);
        }

        public override string GetNamedPipeName(string enlistmentRoot)
        {
            return MacPlatform.GetNamedPipeNameImplementation(enlistmentRoot);
        }

        public override FileBasedLock CreateFileBasedLock(
            PhysicalFileSystem fileSystem,
            ITracer tracer,
            string lockPath)
        {
            return new MacFileBasedLock(fileSystem, tracer, lockPath);
        }

        public override string GetUpgradeProtectedDataDirectory()
        {
            return UpgradeProtectedDataDirectory;
        }

        public override string GetUpgradeHighestAvailableVersionDirectory()
        {
            return GetUpgradeHighestAvailableVersionDirectoryImplementation();
        }

        /// <summary>
        /// This is the directory in which the upgradelogs directory should go.
        /// There can be multiple logs directories, so here we return the containing
        //  directory.
        /// </summary>
        public override string GetUpgradeLogDirectoryParentDirectory()
        {
            return this.GetUpgradeNonProtectedDataDirectory();
        }

        public override Dictionary<string, string> GetPhysicalDiskInfo(string path, bool sizeStatsOnly)
        {
            // DiskUtil will return disk statistics in xml format
            ProcessResult processResult = ProcessHelper.Run("diskutil", "info -plist /", true);
            Dictionary<string, string> result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(processResult.Output))
            {
                result.Add("DiskUtilError", processResult.Errors);
                return result;
            }

            try
            {
                // Parse the XML looking for FilesystemType
                XDocument xmlDoc = XDocument.Parse(processResult.Output);
                XElement filesystemTypeValue = xmlDoc.XPathSelectElement("plist/dict/key[text()=\"FilesystemType\"]")?.NextNode as XElement;
                result.Add("FileSystemType", filesystemTypeValue != null ? filesystemTypeValue.Value: "Not Found");
            }
            catch (XmlException ex)
            {
                result.Add("DiskUtilError", ex.ToString());
            }

            return result;
        }

        public override ProductUpgraderPlatformStrategy CreateProductUpgraderPlatformInteractions(
            PhysicalFileSystem fileSystem,
            ITracer tracer)
        {
            return new MacProductUpgraderPlatformStrategy(fileSystem, tracer);
        }

        public override void IsServiceInstalledAndRunning(string name, out bool installed, out bool running)
        {
            string currentUser = this.GetCurrentUser();
            MacDaemonController macDaemonController = new MacDaemonController(new ProcessRunnerImpl());
            List<MacDaemonController.DaemonInfo> daemons;
            if (!macDaemonController.TryGetDaemons(currentUser, out daemons, out string error))
            {
                installed = false;
                running = false;
            }

            MacDaemonController.DaemonInfo gvfsService = daemons.FirstOrDefault(sc => string.Equals(sc.Name, "org.vfsforgit.service"));
            installed = gvfsService != null;
            running = installed && gvfsService.IsRunning;
        }

        public class MacPlatformConstants : POSIXPlatformConstants
        {
            public override string InstallerExtension
            {
                get { return ".dmg"; }
            }

            public override string WorkingDirectoryBackingRootPath
            {
                get { return GVFSConstants.WorkingDirectoryRootName; }
            }

            public override string DotGVFSRoot
            {
                get { return MacPlatform.DotGVFSRoot; }
            }

            public override string GVFSBinDirectoryPath
            {
                get { return Path.Combine("/usr", "local", this.GVFSBinDirectoryName); }
            }

            public override string GVFSBinDirectoryName
            {
                get { return "vfsforgit"; }
            }
        }
    }
}
