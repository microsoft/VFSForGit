using CommandLine;
using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace GVFS.CommandLine
{
    [Verb(DiagnoseVerb.DiagnoseVerbName, HelpText = "Diagnose issues with a GVFS repo")]
    public class DiagnoseVerb : GVFSVerb.ForExistingEnlistment
    {
        private const string DiagnoseVerbName = "diagnose";
        private const string DeprecatedUpgradeLogsDirectory = "Logs";

        private TextWriter diagnosticLogFileWriter;
        private PhysicalFileSystem fileSystem;

        public DiagnoseVerb() : base(false)
        {
            this.fileSystem = new PhysicalFileSystem();
        }

        protected override string VerbName
        {
            get { return DiagnoseVerbName; }
        }

        protected override void Execute(GVFSEnlistment enlistment)
        {
            string diagnosticsRoot = Path.Combine(enlistment.DotGVFSRoot, "diagnostics");

            if (!Directory.Exists(diagnosticsRoot))
            {
                Directory.CreateDirectory(diagnosticsRoot);
            }

            string archiveFolderPath = Path.Combine(diagnosticsRoot, "gvfs_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(archiveFolderPath);

            using (FileStream diagnosticLogFile = new FileStream(Path.Combine(archiveFolderPath, "diagnostics.log"), FileMode.CreateNew))
            using (this.diagnosticLogFileWriter = new StreamWriter(diagnosticLogFile))
            {
                this.WriteMessage("Collecting diagnostic info into temp folder " + archiveFolderPath);

                this.WriteMessage(string.Empty);
                this.WriteMessage("gvfs version " + ProcessHelper.GetCurrentProcessVersion());

                GitVersion gitVersion = null;
                string error = null;
                if (!string.IsNullOrEmpty(enlistment.GitBinPath) && GitProcess.TryGetVersion(enlistment.GitBinPath, out gitVersion, out error))
                {
                    this.WriteMessage("git version " + gitVersion.ToString());
                }
                else
                {
                    this.WriteMessage("Could not determine git version. " + error);
                }

                this.WriteMessage(enlistment.GitBinPath);
                this.WriteMessage(string.Empty);
                this.WriteMessage("Enlistment root: " + enlistment.EnlistmentRoot);
                this.WriteMessage("Cache Server: " + CacheServerResolver.GetCacheServerFromConfig(enlistment));

                string localCacheRoot;
                string gitObjectsRoot;
                this.GetLocalCachePaths(enlistment, out localCacheRoot, out gitObjectsRoot);
                string actualLocalCacheRoot = !string.IsNullOrWhiteSpace(localCacheRoot) ? localCacheRoot : gitObjectsRoot;
                this.WriteMessage("Local Cache: " + actualLocalCacheRoot);
                this.WriteMessage(string.Empty);

                this.PrintDiskSpaceInfo(actualLocalCacheRoot, this.EnlistmentRootPathParameter);

                this.RecordVersionInformation();

                this.ShowStatusWhileRunning(
                    () =>
                        this.RunAndRecordGVFSVerb<StatusVerb>(archiveFolderPath, "gvfs_status.txt") != ReturnCode.Success ||
                        this.RunAndRecordGVFSVerb<UnmountVerb>(archiveFolderPath, "gvfs_unmount.txt", verb => verb.SkipLock = true) == ReturnCode.Success,
                    "Unmounting",
                    suppressGvfsLogMessage: true);

                this.ShowStatusWhileRunning(
                    () =>
                    {
                        // .gvfs
                        this.CopyAllFiles(enlistment.EnlistmentRoot, archiveFolderPath, GVFSPlatform.Instance.Constants.DotGVFSRoot, copySubFolders: false);

                        // driver
                        if (this.FlushKernelDriverLogs())
                        {
                             string kernelLogsFolderPath = GVFSPlatform.Instance.KernelDriver.LogsFolderPath;

                            // This copy sometimes fails because the OS has an exclusive lock on the etl files. The error is not actionable
                            // for the user so we don't write the error message to stdout, just to our own log file.
                            this.CopyAllFiles(Path.GetDirectoryName(kernelLogsFolderPath), archiveFolderPath, Path.GetFileName(kernelLogsFolderPath), copySubFolders: false, hideErrorsFromStdout: true);
                        }

                        // .git
                        this.CopyAllFiles(enlistment.WorkingDirectoryRoot, archiveFolderPath, GVFSConstants.DotGit.Root, copySubFolders: false);
                        this.CopyAllFiles(enlistment.WorkingDirectoryRoot, archiveFolderPath, GVFSConstants.DotGit.Hooks.Root, copySubFolders: false);
                        this.CopyAllFiles(enlistment.WorkingDirectoryRoot, archiveFolderPath, GVFSConstants.DotGit.Info.Root, copySubFolders: false);
                        this.CopyAllFiles(enlistment.WorkingDirectoryRoot, archiveFolderPath, GVFSConstants.DotGit.Logs.Root, copySubFolders: true);
                        this.CopyAllFiles(enlistment.WorkingDirectoryRoot, archiveFolderPath, GVFSConstants.DotGit.Refs.Root, copySubFolders: true);
                        this.CopyAllFiles(enlistment.WorkingDirectoryRoot, archiveFolderPath, GVFSConstants.DotGit.Objects.Info.Root, copySubFolders: false);
                        this.LogDirectoryEnumeration(enlistment.WorkingDirectoryRoot, Path.Combine(archiveFolderPath, GVFSConstants.DotGit.Objects.Root), GVFSConstants.DotGit.Objects.Pack.Root, "packs-local.txt");
                        this.LogLooseObjectCount(enlistment.WorkingDirectoryRoot, Path.Combine(archiveFolderPath, GVFSConstants.DotGit.Objects.Root), GVFSConstants.DotGit.Objects.Root, "objects-local.txt");

                        // databases
                        this.CopyAllFiles(enlistment.DotGVFSRoot, Path.Combine(archiveFolderPath, GVFSPlatform.Instance.Constants.DotGVFSRoot), GVFSConstants.DotGVFS.Databases.Name, copySubFolders: false);

                        // local cache
                        this.CopyLocalCacheData(archiveFolderPath, localCacheRoot, gitObjectsRoot);

                        // corrupt objects
                        this.CopyAllFiles(enlistment.DotGVFSRoot, Path.Combine(archiveFolderPath, GVFSPlatform.Instance.Constants.DotGVFSRoot), GVFSConstants.DotGVFS.CorruptObjectsName, copySubFolders: false);

                        // service
                        this.CopyAllFiles(
                            GVFSPlatform.Instance.GetLogsRootForGVFS(),
                            archiveFolderPath,
                            this.ServiceName,
                            copySubFolders: true);

                        // service ui
                        this.CopyAllFiles(
                            GVFSPlatform.Instance.GetLogsRootForGVFS(),
                            archiveFolderPath,
                            GVFSConstants.Service.UIName,
                            copySubFolders: true);

                        if (GVFSPlatform.Instance.UnderConstruction.SupportsGVFSUpgrade)
                        {
                            // upgrader
                            this.CopyAllFiles(
                                ProductUpgraderInfo.GetParentLogDirectoryPath(),
                                archiveFolderPath,
                                DeprecatedUpgradeLogsDirectory,
                                copySubFolders: true,
                                targetFolderName: Path.Combine(ProductUpgraderInfo.UpgradeDirectoryName, DeprecatedUpgradeLogsDirectory));

                            this.CopyAllFiles(
                                ProductUpgraderInfo.GetParentLogDirectoryPath(),
                                archiveFolderPath,
                                ProductUpgraderInfo.LogDirectory,
                                copySubFolders: true,
                                targetFolderName: Path.Combine(ProductUpgraderInfo.UpgradeDirectoryName, ProductUpgraderInfo.LogDirectory));

                            this.LogDirectoryEnumeration(
                                ProductUpgraderInfo.GetUpgradeProtectedDataDirectory(),
                                Path.Combine(archiveFolderPath, ProductUpgraderInfo.UpgradeDirectoryName),
                                ProductUpgraderInfo.DownloadDirectory,
                                "downloaded-assets.txt");
                        }

                        if (GVFSPlatform.Instance.UnderConstruction.SupportsGVFSConfig)
                        {
                            this.CopyFile(GVFSPlatform.Instance.GetDataRootForGVFS(), archiveFolderPath, LocalGVFSConfig.FileName);
                        }

                        if (!GVFSPlatform.Instance.TryCopyPanicLogs(archiveFolderPath, out string errorMessage))
                        {
                            this.WriteMessage(errorMessage);
                        }

                        return true;
                    },
                    "Copying logs");

                this.ShowStatusWhileRunning(
                    () => this.RunAndRecordGVFSVerb<MountVerb>(archiveFolderPath, "gvfs_mount.txt") == ReturnCode.Success,
                    "Mounting",
                    suppressGvfsLogMessage: true);

                this.CopyAllFiles(enlistment.DotGVFSRoot, Path.Combine(archiveFolderPath, GVFSPlatform.Instance.Constants.DotGVFSRoot), "logs", copySubFolders: false);
            }

            string zipFilePath = archiveFolderPath + ".zip";
            this.ShowStatusWhileRunning(
                () =>
                {
                    ZipFile.CreateFromDirectory(archiveFolderPath, zipFilePath);
                    this.fileSystem.DeleteDirectory(archiveFolderPath);

                    return true;
                },
                "Creating zip file",
                suppressGvfsLogMessage: true);

            this.Output.WriteLine();
            this.Output.WriteLine("Diagnostics complete. All of the gathered info, as well as all of the output above, is captured in");
            this.Output.WriteLine(zipFilePath);
        }

        private void WriteMessage(string message, bool skipStdout = false)
        {
            message = message.TrimEnd('\r', '\n');

            if (!skipStdout)
            {
                this.Output.WriteLine(message);
            }

            this.diagnosticLogFileWriter.WriteLine(message);
        }

        private void RecordVersionInformation()
        {
            string information = GVFSPlatform.Instance.GetOSVersionInformation();
            this.diagnosticLogFileWriter.WriteLine(information);
        }

        private void CopyFile(
            string sourceRoot,
            string targetRoot,
            string fileName)
        {
            string sourceFile = Path.Combine(sourceRoot, fileName);
            string targetFile = Path.Combine(targetRoot, fileName);

            try
            {
                if (!File.Exists(sourceFile))
                {
                    return;
                }

                File.Copy(sourceFile, targetFile);
            }
            catch (Exception e)
            {
                this.WriteMessage(
                    string.Format(
                        "Failed to copy file {0} in {1} with exception {2}",
                        fileName,
                        sourceRoot,
                        e));
            }
        }

        private void CopyAllFiles(
            string sourceRoot,
            string targetRoot,
            string folderName,
            bool copySubFolders,
            bool hideErrorsFromStdout = false,
            string targetFolderName = null)
        {
            string sourceFolder = Path.Combine(sourceRoot, folderName);
            string targetFolder = Path.Combine(targetRoot, targetFolderName ?? folderName);

            try
            {
                if (!Directory.Exists(sourceFolder))
                {
                    return;
                }

                this.RecursiveFileCopyImpl(sourceFolder, targetFolder, copySubFolders, hideErrorsFromStdout);
            }
            catch (Exception e)
            {
                this.WriteMessage(
                    string.Format(
                        "Failed to copy folder {0} in {1} with exception {2}. copySubFolders: {3}",
                        folderName,
                        sourceRoot,
                        e,
                        copySubFolders),
                    hideErrorsFromStdout);
            }
        }

        private void GetLocalCachePaths(GVFSEnlistment enlistment, out string localCacheRoot, out string gitObjectsRoot)
        {
            localCacheRoot = null;
            gitObjectsRoot = null;

            try
            {
                using (ITracer tracer = new JsonTracer(GVFSConstants.GVFSEtwProviderName, "DiagnoseVerb"))
                {
                    string error;
                    if (RepoMetadata.TryInitialize(tracer, Path.Combine(enlistment.EnlistmentRoot, GVFSPlatform.Instance.Constants.DotGVFSRoot), out error))
                    {
                        RepoMetadata.Instance.TryGetLocalCacheRoot(out localCacheRoot, out error);
                        RepoMetadata.Instance.TryGetGitObjectsRoot(out gitObjectsRoot, out error);
                    }
                    else
                    {
                        this.WriteMessage("Failed to determine local cache path and git objects root, RepoMetadata error: " + error);
                    }
                }
            }
            catch (Exception e)
            {
                this.WriteMessage(string.Format("Failed to determine local cache path and git objects root, Exception: {0}", e));
            }
            finally
            {
                RepoMetadata.Shutdown();
            }
        }

        private void CopyLocalCacheData(string archiveFolderPath, string localCacheRoot, string gitObjectsRoot)
        {
            try
            {
                string localCacheArchivePath = Path.Combine(archiveFolderPath, GVFSConstants.DefaultGVFSCacheFolderName);
                Directory.CreateDirectory(localCacheArchivePath);

                if (!string.IsNullOrWhiteSpace(localCacheRoot))
                {
                    // Copy all mapping.dat files in the local cache folder (i.e. mapping.dat, mapping.dat.tmp, mapping.dat.lock)
                    foreach (string filePath in Directory.EnumerateFiles(localCacheRoot, "mapping.dat*"))
                    {
                        string fileName = Path.GetFileName(filePath);
                        try
                        {
                            File.Copy(filePath, Path.Combine(localCacheArchivePath, fileName));
                        }
                        catch (Exception e)
                        {
                            this.WriteMessage(string.Format(
                                "Failed to copy '{0}' from {1} to {2} with exception {3}",
                                fileName,
                                localCacheRoot,
                                archiveFolderPath,
                                e));
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(gitObjectsRoot))
                {
                    this.LogDirectoryEnumeration(gitObjectsRoot, localCacheArchivePath, GVFSConstants.DotGit.Objects.Pack.Name, "packs-cached.txt");
                    this.LogLooseObjectCount(gitObjectsRoot, localCacheArchivePath, string.Empty, "objects-cached.txt");

                    // Store all commit-graph files
                    this.CopyAllFiles(gitObjectsRoot, localCacheArchivePath, GVFSConstants.DotGit.Objects.Info.Root, copySubFolders: true);
                }
            }
            catch (Exception e)
            {
                this.WriteMessage(string.Format("Failed to copy local cache data with exception: {0}", e));
            }
        }

        private void LogDirectoryEnumeration(string sourceRoot, string targetRoot, string folderName, string logfile)
        {
            try
            {
                if (!Directory.Exists(targetRoot))
                {
                    Directory.CreateDirectory(targetRoot);
                }

                string folder = Path.Combine(sourceRoot, folderName);
                string targetLog = Path.Combine(targetRoot, logfile);

                List<string> lines = new List<string>();

                if (Directory.Exists(folder))
                {
                    DirectoryInfo packDirectory = new DirectoryInfo(folder);

                    lines.Add($"Contents of {folder}:");
                    foreach (FileInfo file in packDirectory.EnumerateFiles())
                    {
                        lines.Add($"{file.Name, -70} {file.Length, 16}");
                    }
                }

                File.WriteAllLines(targetLog, lines.ToArray());
            }
            catch (Exception e)
            {
                this.WriteMessage(string.Format(
                    "Failed to log file sizes for {0} in {1} with exception {2}. logfile: {3}",
                    folderName,
                    sourceRoot,
                    e,
                    logfile));
            }
        }

        private void LogLooseObjectCount(string sourceRoot, string targetRoot, string folderName, string logfile)
        {
            try
            {
                if (!Directory.Exists(targetRoot))
                {
                    Directory.CreateDirectory(targetRoot);
                }

                string objectFolder = Path.Combine(sourceRoot, folderName);
                string targetLog = Path.Combine(targetRoot, logfile);

                List<string> lines = new List<string>();

                if (Directory.Exists(objectFolder))
                {
                    DirectoryInfo objectDirectory = new DirectoryInfo(objectFolder);

                    int countLoose = 0;
                    int countFolders = 0;

                    lines.Add($"Object directory stats for {objectFolder}:");

                    foreach (DirectoryInfo directory in objectDirectory.EnumerateDirectories())
                    {
                        if (GitObjects.IsLooseObjectsDirectory(directory.Name))
                        {
                            countFolders++;
                            int numObjects = directory.EnumerateFiles().Count();
                            lines.Add($"{directory.Name} : {numObjects, 7} objects");
                            countLoose += numObjects;
                        }
                    }

                    lines.Add($"Total: {countLoose} loose objects");
                }

                File.WriteAllLines(targetLog, lines.ToArray());
            }
            catch (Exception e)
            {
                this.WriteMessage(string.Format(
                    "Failed to log loose object count for {0} in {1} with exception {2}. logfile: {3}",
                    folderName,
                    sourceRoot,
                    e,
                    logfile));
            }
        }

        private void RecursiveFileCopyImpl(string sourcePath, string targetPath, bool copySubFolders, bool hideErrorsFromStdout)
        {
            if (!Directory.Exists(targetPath))
            {
                Directory.CreateDirectory(targetPath);
            }

            foreach (string filePath in Directory.EnumerateFiles(sourcePath))
            {
                string fileName = Path.GetFileName(filePath);
                try
                {
                    string sourceFilePath = Path.Combine(sourcePath, fileName);
                    if (!GVFSPlatform.Instance.FileSystem.IsSocket(sourceFilePath) &&
                        !GVFSPlatform.Instance.FileSystem.IsExecutable(sourceFilePath))
                    {
                        File.Copy(
                            Path.Combine(sourcePath, fileName),
                            Path.Combine(targetPath, fileName));
                    }
                }
                catch (Exception e)
                {
                    this.WriteMessage(
                        string.Format(
                            "Failed to copy '{0}' in {1} with exception {2}",
                            fileName,
                            sourcePath,
                            e),
                        hideErrorsFromStdout);
                }
            }

            if (copySubFolders)
            {
                DirectoryInfo dir = new DirectoryInfo(sourcePath);
                foreach (DirectoryInfo subdir in dir.GetDirectories())
                {
                    string targetFolderPath = Path.Combine(targetPath, subdir.Name);
                    try
                    {
                        this.RecursiveFileCopyImpl(subdir.FullName, targetFolderPath, copySubFolders, hideErrorsFromStdout);
                    }
                    catch (Exception e)
                    {
                        this.WriteMessage(
                            string.Format(
                                "Failed to copy subfolder '{0}' to '{1}' with exception {2}",
                                subdir.FullName,
                                targetFolderPath,
                                e),
                            hideErrorsFromStdout);
                    }
                }
            }
        }

        private ReturnCode RunAndRecordGVFSVerb<TVerb>(string archiveFolderPath, string outputFileName, Action<TVerb> configureVerb = null)
            where TVerb : GVFSVerb, new()
        {
            try
            {
                using (FileStream file = new FileStream(Path.Combine(archiveFolderPath, outputFileName), FileMode.CreateNew))
                using (StreamWriter writer = new StreamWriter(file))
                {
                    return this.Execute<TVerb>(
                        this.EnlistmentRootPathParameter,
                        verb =>
                        {
                            if (configureVerb != null)
                            {
                                configureVerb(verb);
                            }

                            verb.Output = writer;
                        });
                }
            }
            catch (Exception e)
            {
                this.WriteMessage(string.Format(
                    "Verb {0} failed with exception {1}",
                    typeof(TVerb),
                    e));

                return ReturnCode.GenericError;
            }
        }

        private bool FlushKernelDriverLogs()
        {
            string errors;
            bool flushSuccess = GVFSPlatform.Instance.KernelDriver.TryFlushLogs(out errors);
            this.diagnosticLogFileWriter.WriteLine(errors);
            return flushSuccess;
        }

        private void PrintDiskSpaceInfo(string localCacheRoot, string enlistmentRootParameter)
        {
            try
            {
                string enlistmentNormalizedPathRoot;
                string localCacheNormalizedPathRoot;
                string enlistmentErrorMessage;
                string localCacheErrorMessage;

                bool enlistmentSuccess = GVFSPlatform.Instance.TryGetNormalizedPathRoot(enlistmentRootParameter, out enlistmentNormalizedPathRoot, out enlistmentErrorMessage);
                bool localCacheSuccess = GVFSPlatform.Instance.TryGetNormalizedPathRoot(localCacheRoot, out localCacheNormalizedPathRoot, out localCacheErrorMessage);

                if (!enlistmentSuccess || !localCacheSuccess)
                {
                    this.WriteMessage("Failed to acquire disk space information:");
                    if (!string.IsNullOrEmpty(enlistmentErrorMessage))
                    {
                        this.WriteMessage(enlistmentErrorMessage);
                    }

                    if (!string.IsNullOrEmpty(localCacheErrorMessage))
                    {
                        this.WriteMessage(localCacheErrorMessage);
                    }

                    this.WriteMessage(string.Empty);
                    return;
                }

                DriveInfo enlistmentDrive = new DriveInfo(enlistmentNormalizedPathRoot);
                string enlistmentDriveDiskSpace = this.FormatByteCount(enlistmentDrive.AvailableFreeSpace);

                if (string.Equals(enlistmentNormalizedPathRoot, localCacheNormalizedPathRoot, GVFSPlatform.Instance.Constants.PathComparison))
                {
                    this.WriteMessage("Available space on " + enlistmentDrive.Name + " drive(enlistment and local cache): " + enlistmentDriveDiskSpace);
                }
                else
                {
                    this.WriteMessage("Available space on " + enlistmentDrive.Name + " drive(enlistment): " + enlistmentDriveDiskSpace);

                    DriveInfo cacheDrive = new DriveInfo(localCacheRoot);
                    string cacheDriveDiskSpace = this.FormatByteCount(cacheDrive.AvailableFreeSpace);
                    this.WriteMessage("Available space on " + cacheDrive.Name + " drive(local cache): " + cacheDriveDiskSpace);
                }

                this.WriteMessage(string.Empty);
            }
            catch (Exception e)
            {
                this.WriteMessage("Failed to acquire disk space information, exception: " + e.ToString());
                this.WriteMessage(string.Empty);
            }
        }

        private string FormatByteCount(double byteCount)
        {
            const int Divisor = 1024;
            const string ByteCountFormat = "0.00";
            string[] unitStrings = { " B", " KB", " MB", " GB", " TB" };

            int unitIndex = 0;

            while (byteCount >= Divisor && unitIndex < unitStrings.Length - 1)
            {
                unitIndex++;
                byteCount = byteCount / Divisor;
            }

            return byteCount.ToString(ByteCountFormat) + unitStrings[unitIndex];
        }
    }
}
