using CommandLine;
using GVFS.Common;
using GVFS.Common.Database;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GVFS.CommandLine
{
    [Verb(IncludeVerb.IncludeVerbName, HelpText = "List, add, or Remove from the list of folder that are included to project")]
    public class IncludeVerb : GVFSVerb.ForExistingEnlistment
    {
        private const string IncludeVerbName = "include";

        [Option(
            'a',
            "add",
            Required = false,
            Default = "",
            HelpText = "A semicolon-delimited list of folders to include. Wildcards are not supported.")]
        public string Add { get; set; }

        [Option(
            'r',
            "remove",
            Required = false,
            Default = "",
            HelpText = "A semicolon-delimited list of folders to remove for being included. Wildcards are not supported.")]
        public string Remove { get; set; }

        [Option(
            'l',
            "list",
            Required = false,
            Default = false,
            HelpText = "List of folders to for being included in projection.")]
        public bool List { get; set; }

        protected override string VerbName => IncludeVerbName;

        protected override void Execute(GVFSEnlistment enlistment)
        {
            using (JsonTracer tracer = new JsonTracer(GVFSConstants.GVFSEtwProviderName, "Include"))
            {
                try
                {
                    tracer.AddLogFileEventListener(
                        GVFSEnlistment.GetNewGVFSLogFileName(enlistment.GVFSLogsRoot, GVFSConstants.LogFileTypes.Include),
                        EventLevel.Informational,
                        Keywords.Any);

                    using (GVFSDatabase database = new GVFSDatabase(new PhysicalFileSystem(), enlistment.EnlistmentRoot, new SqliteDatabase()))
                    {
                        IncludedFolderTable includedFolderTable = new IncludedFolderTable(database);

                        if (this.List)
                        {
                            List<string> directories = includedFolderTable.GetAll();
                            if (directories.Count == 0)
                            {
                                this.Output.WriteLine("No folders in included list.");
                            }
                            else
                            {
                                foreach (string directory in directories)
                                {
                                    this.Output.WriteLine(directory);
                                }
                            }

                            return;
                        }

                        // Make sure there is a clean git status before allowing inclusions to change
                        this.CheckGitStatus(tracer, enlistment);

                        if (!string.IsNullOrEmpty(this.Remove))
                        {
                            foreach (string directoryPath in this.Remove.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                includedFolderTable.Remove(directoryPath);
                            }
                        }

                        if (!string.IsNullOrEmpty(this.Add))
                        {
                            foreach (string directoryPath in this.Add.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                includedFolderTable.Add(directoryPath);
                            }
                        }
                    }

                    // Force a projection update to get the current inclusion set
                    this.ForceProjectionChange(tracer, enlistment);
                }
                catch (Exception e)
                {
                    tracer.RelatedError(e.Message);
                }
            }
        }

        private void ForceProjectionChange(ITracer tracer, GVFSEnlistment enlistment)
        {
            string errorMessage = null;

            if (!this.ShowStatusWhileRunning(
                () =>
                {
                    GitProcess git = new GitProcess(enlistment);
                    GitProcess.Result checkoutResult = git.ForceCheckout("HEAD");

                    errorMessage = checkoutResult.Errors;
                    return checkoutResult.ExitCodeIsSuccess;
                },
                "Forcing a projection change",
                suppressGvfsLogMessage: true))
            {
                this.WriteMessage(tracer, "Failed to change projection: " + errorMessage);
            }
        }

        private void CheckGitStatus(ITracer tracer, GVFSEnlistment enlistment)
        {
            GitProcess.Result statusResult = null;
            if (!this.ShowStatusWhileRunning(
                () =>
                {
                    GitProcess git = new GitProcess(enlistment);
                    statusResult = git.Status(allowObjectDownloads: false, useStatusCache: false, showUntracked: true);
                    if (statusResult.ExitCodeIsFailure)
                    {
                        return false;
                    }

                    if (!statusResult.Output.Contains("nothing to commit, working tree clean"))
                    {
                        return false;
                    }

                    return true;
                },
                "Running git status",
                suppressGvfsLogMessage: true))
            {
                this.Output.WriteLine();

                if (statusResult.ExitCodeIsFailure)
                {
                    this.WriteMessage(tracer, "Failed to run git status: " + statusResult.Errors);
                }
                else
                {
                    this.WriteMessage(tracer, statusResult.Output);
                    this.WriteMessage(tracer, "git status reported that you have dirty files");
                    this.WriteMessage(tracer, "Either commit your changes or reset and clean");
                }

                this.ReportErrorAndExit(tracer, "Include was aborted");
            }
        }

        private void WriteMessage(ITracer tracer, string message)
        {
            this.Output.WriteLine(message);
            tracer.RelatedEvent(
                EventLevel.Informational,
                IncludeVerbName,
                new EventMetadata
                {
                    { TracingConstants.MessageKey.InfoMessage, message }
                });
        }
    }
}
