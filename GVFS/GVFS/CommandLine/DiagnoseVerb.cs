using CommandLine;
using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Physical.FileSystem;
using GVFS.GVFlt;
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
                this.WriteMessage("Objects URL: " + enlistment.ObjectsEndpointUrl);
                this.WriteMessage(string.Empty);

                this.WriteMessage("Copying .gvfs folder...");
                this.CopyAllFiles(enlistment.EnlistmentRoot, archiveFolderPath, GVFSConstants.DotGVFSPath, copySubFolders: false);

                this.WriteMessage("Copying GVFlt logs...");
                string system32LogFilesPath = Environment.ExpandEnvironmentVariables(System32LogFilesRoot);
                this.CopyAllFiles(system32LogFilesPath, archiveFolderPath, GVFltLogFolderName, copySubFolders: false);

                this.WriteMessage("Checking on GVFS...");
                this.RunAndRecordGVFSVerb<LogVerb>(archiveFolderPath, "gvfs_log.txt");
                ReturnCode statusResult = this.RunAndRecordGVFSVerb<StatusVerb>(archiveFolderPath, "gvfs_status.txt");

                if (statusResult == ReturnCode.Success)
                {
                    this.WriteMessage("GVFS is mounted. Unmounting so we can read files that GVFS has locked...");
                    this.RunAndRecordGVFSVerb<UnmountVerb>(archiveFolderPath, "gvfs_unmount.txt");
                }
                else
                {
                    this.WriteMessage("GVFS was not mounted.");
                }

                this.WriteMessage("Copying .git folder...");
                this.CopyAllFiles(enlistment.WorkingDirectoryRoot, archiveFolderPath, GVFSConstants.DotGit.Root, copySubFolders: false);
                this.CopyAllFiles(enlistment.WorkingDirectoryRoot, archiveFolderPath, GVFSConstants.DotGit.Hooks.Root, copySubFolders: false);
                this.CopyAllFiles(enlistment.WorkingDirectoryRoot, archiveFolderPath, GVFSConstants.DotGit.Info.Root, copySubFolders: false);
                this.CopyAllFiles(enlistment.WorkingDirectoryRoot, archiveFolderPath, GVFSConstants.DotGit.Logs.Root, copySubFolders: true);

                this.CopyEsentDatabase<Guid, GVFltCallbacks.BackgroundGitUpdate>(
                    enlistment.DotGVFSRoot,
                    Path.Combine(archiveFolderPath, GVFSConstants.DotGVFSPath),
                    GVFSConstants.DatabaseNames.BackgroundGitUpdates);
                this.CopyEsentDatabase<string, bool>(
                    enlistment.DotGVFSRoot,
                    Path.Combine(archiveFolderPath, GVFSConstants.DotGVFSPath),
                    GVFSConstants.DatabaseNames.DoNotProject);
                this.CopyEsentDatabase<string, long>(
                    enlistment.DotGVFSRoot,
                    Path.Combine(archiveFolderPath, GVFSConstants.DotGVFSPath),
                    GVFSConstants.DatabaseNames.BlobSizes);
                this.CopyEsentDatabase<string, string>(
                    enlistment.DotGVFSRoot,
                    Path.Combine(archiveFolderPath, GVFSConstants.DotGVFSPath),
                    GVFSConstants.DatabaseNames.RepoMetadata);

                this.WriteMessage(string.Empty);
                this.WriteMessage("Remounting GVFS...");
                ReturnCode mountResult = this.RunAndRecordGVFSVerb<MountVerb>(archiveFolderPath, "gvfs_mount.txt");
                if (mountResult == ReturnCode.Success)
                {
                    this.WriteMessage("Mount succeeded");
                }
                else
                {
                    this.WriteMessage("Failed to remount. The reason for failure was captured.");
                }

                this.CopyAllFiles(enlistment.DotGVFSRoot, Path.Combine(archiveFolderPath, GVFSConstants.DotGVFSPath), "logs", copySubFolders: false);
            }

            string zipFilePath = archiveFolderPath + ".zip";
            ZipFile.CreateFromDirectory(archiveFolderPath, zipFilePath);
            PhysicalFileSystem.RecursiveDelete(archiveFolderPath);

            this.Output.WriteLine();
            this.Output.WriteLine("Diagnostics complete. All of the gathered info, as well as all of the output above, is captured in");
            this.Output.WriteLine(zipFilePath);
            this.Output.WriteLine();
            this.Output.WriteLine("If you are experiencing an issue, please email the GVFS team with your repro steps and include this zip file.");
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
                    this.WriteMessage(string.Format("Skipping {0}, folder does not exist", sourceFolder));
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

        private ReturnCode RunAndRecordGVFSVerb<TVerb>(string archiveFolderPath, string outputFileName)
            where TVerb : GVFSVerb, new()
        {
            try
            {
                using (FileStream file = new FileStream(Path.Combine(archiveFolderPath, outputFileName), FileMode.CreateNew))
                using (StreamWriter writer = new StreamWriter(file))
                {
                    return GVFSVerb.Execute<TVerb>(this.EnlistmentRootPath, verb => verb.Output = writer);
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
                        this.WriteMessage(string.Format(
                            "Found {0} entries in {1}",
                            dictionary.Count,
                            databaseName));

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
    }
}
