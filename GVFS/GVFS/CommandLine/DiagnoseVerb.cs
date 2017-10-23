using CommandLine;
using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Http;
using Microsoft.Isam.Esent.Collections.Generic;
using System;
using System.IO;
using System.IO.Compression;

namespace GVFS.CommandLine
{
    [Verb(DiagnoseVerb.DiagnoseVerbName, HelpText = "Diagnose issues with a GVFS repo")]
    public class DiagnoseVerb : GVFSVerb.ForExistingEnlistment
    {
        private const string DiagnoseVerbName = "diagnose";

        private const string System32LogFilesRoot = @"%SystemRoot%\System32\LogFiles";
        private const string GVFltLogFolderName = "GvFlt";

        // From "Autologger" section of gvflt.inf
        private const string GvFltLoggerGuid = "5f6d2558-5c94-48f9-add0-65bc678aa091";
        private const string GvFltLoggerSessionName = "Microsoft-Windows-Git-Filter-Log";

        private TextWriter diagnosticLogFileWriter;

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
                this.WriteMessage(GitProcess.Version(enlistment).Output);
                this.WriteMessage(GitProcess.GetInstalledGitBinPath());
                this.WriteMessage(string.Empty);
                this.WriteMessage("Enlistment root: " + enlistment.EnlistmentRoot);
                this.WriteMessage("Repo URL: " + enlistment.RepoUrl);
                this.WriteMessage("Cache Server: " + CacheServerResolver.GetUrlFromConfig(enlistment));

                this.WriteMessage(string.Empty);

                this.WriteAntiVirusExclusions(enlistment.EnlistmentRoot, archiveFolderPath, "DefenderExclusionInfo.txt");

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
                        this.CopyAllFiles(enlistment.EnlistmentRoot, archiveFolderPath, GVFSConstants.DotGVFS.Root, copySubFolders: false);

                        // gvflt
                        this.FlushGvFltLogBuffers();
                        string system32LogFilesPath = Environment.ExpandEnvironmentVariables(System32LogFilesRoot);
                        this.CopyAllFiles(system32LogFilesPath, archiveFolderPath, GVFltLogFolderName, copySubFolders: false);

                        // .git
                        this.CopyAllFiles(enlistment.WorkingDirectoryRoot, archiveFolderPath, GVFSConstants.DotGit.Root, copySubFolders: false);
                        this.CopyAllFiles(enlistment.WorkingDirectoryRoot, archiveFolderPath, GVFSConstants.DotGit.Hooks.Root, copySubFolders: false);
                        this.CopyAllFiles(enlistment.WorkingDirectoryRoot, archiveFolderPath, GVFSConstants.DotGit.Info.Root, copySubFolders: false);
                        this.CopyAllFiles(enlistment.WorkingDirectoryRoot, archiveFolderPath, GVFSConstants.DotGit.Logs.Root, copySubFolders: true);
                        this.CopyAllFiles(enlistment.WorkingDirectoryRoot, archiveFolderPath, GVFSConstants.DotGit.Refs.Root, copySubFolders: true);
                        this.CopyAllFiles(enlistment.WorkingDirectoryRoot, archiveFolderPath, GVFSConstants.DotGit.Objects.Info.Root, copySubFolders: false);

                        // databases
                        this.CopyEsentDatabase<string, long>(enlistment.DotGVFSRoot, Path.Combine(archiveFolderPath, GVFSConstants.DotGVFS.Root), GVFSConstants.DotGVFS.BlobSizesName);
                        this.CopyAllFiles(enlistment.DotGVFSRoot, Path.Combine(archiveFolderPath, GVFSConstants.DotGVFS.Root), GVFSConstants.DotGVFS.Databases.Name, copySubFolders: false);

                        // corrupt objects
                        this.CopyAllFiles(enlistment.DotGVFSRoot, Path.Combine(archiveFolderPath, GVFSConstants.DotGVFS.Root), GVFSConstants.DotGVFS.CorruptObjectsName, copySubFolders: false);

                        // service
                        this.CopyAllFiles(
                            Paths.GetServiceDataRoot(string.Empty),
                            archiveFolderPath,
                            this.ServiceName,
                            copySubFolders: true);

                        return true;
                    },
                    "Copying logs");

                this.ShowStatusWhileRunning(
                    () => this.RunAndRecordGVFSVerb<MountVerb>(archiveFolderPath, "gvfs_mount.txt") == ReturnCode.Success,
                    "Mounting",
                    suppressGvfsLogMessage: true);

                this.CopyAllFiles(enlistment.DotGVFSRoot, Path.Combine(archiveFolderPath, GVFSConstants.DotGVFS.Root), "logs", copySubFolders: false);
            }

            string zipFilePath = archiveFolderPath + ".zip";
            this.ShowStatusWhileRunning(
                () =>
                {
                    ZipFile.CreateFromDirectory(archiveFolderPath, zipFilePath);
                    PhysicalFileSystem.RecursiveDelete(archiveFolderPath);

                    return true;
                },
                "Creating zip file",
                suppressGvfsLogMessage: true);

            this.Output.WriteLine();
            this.Output.WriteLine("Diagnostics complete. All of the gathered info, as well as all of the output above, is captured in");
            this.Output.WriteLine(zipFilePath);
        }

        private void WriteMessage(string message)
        {
            message = message.TrimEnd('\r', '\n');

            this.Output.WriteLine(message);
            this.diagnosticLogFileWriter.WriteLine(message);
        }

        private void CopyAllFiles(string sourceRoot, string targetRoot, string folderName, bool copySubFolders)
        {
            string sourceFolder = Path.Combine(sourceRoot, folderName);
            string targetFolder = Path.Combine(targetRoot, folderName);

            try
            {
                if (!Directory.Exists(sourceFolder))
                {
                    return;
                }

                this.RecursiveFileCopyImpl(sourceFolder, targetFolder, copySubFolders);
            }
            catch (Exception e)
            {
                this.WriteMessage(string.Format(
                    "Failed to copy folder {0} in {1} with exception {2}. copySubFolders: {3}",
                    folderName,
                    sourceRoot,
                    e,
                    copySubFolders));
            }
        }

        private void RecursiveFileCopyImpl(string sourcePath, string targetPath, bool copySubFolders)
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
                    string fileExtension = Path.GetExtension(fileName);
                    if (!string.Equals(fileExtension, ".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(
                            Path.Combine(sourcePath, fileName),
                            Path.Combine(targetPath, fileName));
                    }
                }
                catch (Exception e)
                {
                    this.WriteMessage(string.Format(
                        "Failed to copy '{0}' in {1} with exception {2}",
                        fileName,
                        sourcePath,
                        e));
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
                        this.RecursiveFileCopyImpl(subdir.FullName, targetFolderPath, copySubFolders);
                    }
                    catch (Exception e)
                    {
                        this.WriteMessage(string.Format(
                            "Failed to copy subfolder '{0}' to '{1}' with exception {2}",
                            subdir.FullName,
                            targetFolderPath,
                            e));
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

        private void WriteAntiVirusExclusions(string enlistmentRoot, string archiveFolderPath, string outputFileName)
        {
            string filepath = Path.Combine(archiveFolderPath, outputFileName);
            try
            {
                bool isExcluded;
                string error;
                string message = string.Empty;
                if (AntiVirusExclusions.TryGetIsPathExcluded(enlistmentRoot, out isExcluded, out error))
                {
                    message = "Successfully read Defender exclusions. \n ";
                    if (isExcluded)
                    {
                        message += enlistmentRoot + " is excluded.";
                    }
                    else
                    {
                        message += enlistmentRoot + " is not excluded.";
                    }
                }
                else
                {
                    message = "Unable to read Defender exclusions. \n " + error;
                }

                File.WriteAllText(filepath, message);
            }
            catch (Exception exc)
            {
                this.WriteMessage(
                    "Failed to gather Defender exclusion info. \n" +
                    exc.ToString());
            }
        }

        private void CopyEsentDatabase<TKey, TValue>(string sourceFolder, string targetFolder, string databaseName)
            where TKey : IComparable<TKey>
        {
            try
            {
                if (!Directory.Exists(targetFolder))
                {
                    Directory.CreateDirectory(targetFolder);
                }

                using (FileStream outputFile = new FileStream(Path.Combine(targetFolder, databaseName + ".txt"), FileMode.CreateNew))
                using (StreamWriter writer = new StreamWriter(outputFile))
                {
                    using (PersistentDictionary<TKey, TValue> dictionary = new PersistentDictionary<TKey, TValue>(
                        Path.Combine(sourceFolder, databaseName)))
                    {
                        foreach (TKey key in dictionary.Keys)
                        {
                            writer.Write(key);
                            writer.Write(" = ");
                            writer.WriteLine(dictionary[key].ToString());
                        }
                    }
                }
            }
            catch (Exception e)
            {
                this.WriteMessage(string.Format(
                    "Failed to copy database {0} with exception {1}",
                    databaseName,
                    e));
            }

            // Also copy the database files themselves, in case we failed to read the entries above
            this.CopyAllFiles(sourceFolder, targetFolder, databaseName, copySubFolders: false);
        }

        private void FlushGvFltLogBuffers()
        {
            try
            {
                string logfileName;
                uint result = NativeMethods.FlushTraceLogger(GvFltLoggerSessionName, GvFltLoggerGuid, out logfileName);
                if (result != 0)
                {
                    this.WriteMessage(string.Format(
                        "Failed to flush GvFlt log buffers {0}",
                        result));
                }
            }
            catch (Exception e)
            {
                this.WriteMessage(string.Format("Failed to flush GvFlt log buffers, exception: {0}", e));
            }
        }
    }
}
