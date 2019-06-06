using CommandLine;
using GVFS.Common;
using GVFS.Common.Database;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using GVFS.Virtualization;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading.Tasks;

namespace GVFS.CommandLine
{
    [Verb(StatisticsVerb.StatisticsVerbName, HelpText = "Get statistics for the health state of the repository")]
    public class StatisticsVerb : GVFSVerb.ForExistingEnlistment
    {
        private const string StatisticsVerbName = "statistics";

        private const int DirectoryDisplayCount = 5;

        protected override string VerbName
        {
            get { return StatisticsVerbName; }
        }

        protected override void Execute(GVFSEnlistment enlistment)
        {
            List<IPlaceholderData> filePlaceholders = new List<IPlaceholderData>();
            List<IPlaceholderData> folderPlaceholders = new List<IPlaceholderData>();
            string[] modifiedPathsList = { }; // Empty so if the pipe breaks no null pointer exception is created

            /* Failed code for creating a local instance of the modified paths database
            using (JsonTracer tracer = new JsonTracer(GVFSConstants.GVFSEtwProviderName, "Statistics"))
            {
                ModifiedPathsDatabase.TryLoadOrCreate(
                    tracer,
                    Path.Combine(enlistment.DotGVFSRoot, GVFSConstants.DotGVFS.Databases.ModifiedPaths),
                    new PhysicalFileSystem(),
                    out ModifiedPathsDatabase modifiedPathsDatabase,
                    out string err);
            }
            */

            GVFSDatabase database = new GVFSDatabase(new PhysicalFileSystem(), enlistment.EnlistmentRoot, new SqliteDatabase());
            PlaceholderTable placeholderTable = new PlaceholderTable(database);

            placeholderTable.GetAllEntries(out filePlaceholders, out folderPlaceholders);

            using (NamedPipeClient pipeClient = new NamedPipeClient(enlistment.NamedPipeName))
            {
                if (!pipeClient.Connect())
                {
                    this.ReportErrorAndExit("Unable to connect to GVFS.  Try running 'gvfs mount'");
                }

                try
                {
                    NamedPipeMessages.Message modifiedPathsMessage = new NamedPipeMessages.Message(NamedPipeMessages.ModifiedPaths.ListRequest, "1");
                    pipeClient.SendRequest(modifiedPathsMessage);

                    NamedPipeMessages.Message modifiedPathsResponse = pipeClient.ReadResponse();
                    if (!modifiedPathsResponse.Header.Equals(NamedPipeMessages.ModifiedPaths.SuccessResult))
                    {
                        this.Output.WriteLine("Bad response from modified path pipe: " + modifiedPathsResponse.Header);
                        return;
                    }

                    modifiedPathsList = modifiedPathsResponse.Body.Split('\0');
                    modifiedPathsList = modifiedPathsList.Take(modifiedPathsList.Length - 1).ToArray();
                }
                catch (BrokenPipeException e)
                {
                    this.ReportErrorAndExit("Unable to communicate with GVFS: " + e.ToString());
                }
            }

            int trackedFilesCount = 0;
            int placeholderCount = filePlaceholders.Count + folderPlaceholders.Count;
            int modifiedPathsCount = modifiedPathsList.Length;
            Dictionary<string, int> trackedFilesLookup = new Dictionary<string, int>();
            Dictionary<string, int> topLevelDirectoryHydrationLookup = new Dictionary<string, int>();

            GitProcess gitProcess = new GitProcess(enlistment);
            GitProcess.Result result = gitProcess.LsTree(
                GVFSConstants.DotGit.HeadName,
                line =>
                {
                    string topDir = this.TopDirectory(this.TrimGitIndexLine(line));
                    trackedFilesLookup[topDir] = trackedFilesLookup.ContainsKey(topDir) ? trackedFilesLookup[topDir] + 1 : 1;
                    trackedFilesCount++;
                },
                recursive: true);

            foreach (string path in modifiedPathsList)
            {
                string topDir = this.TopDirectory(path);
                topLevelDirectoryHydrationLookup[topDir] = topLevelDirectoryHydrationLookup.ContainsKey(topDir) ? topLevelDirectoryHydrationLookup[topDir] + 1 : 1;
            }

            foreach (IPlaceholderData placeholder in filePlaceholders.Concat(folderPlaceholders))
            {
                string topDir = this.TopDirectory(placeholder.Path);
                topLevelDirectoryHydrationLookup[topDir] = topLevelDirectoryHydrationLookup.ContainsKey(topDir) ? topLevelDirectoryHydrationLookup[topDir] + 1 : 1;
            }

            string trackedFilesCountFormatted = trackedFilesCount.ToString("N0");
            string placeholderCountFormatted = placeholderCount.ToString("N0");
            string modifiedPathsCountFormatted = modifiedPathsCount.ToString("N0");

            int longest = Math.Max(trackedFilesCountFormatted.Length, placeholderCountFormatted.Length);
            longest = Math.Max(longest, modifiedPathsCountFormatted.Length);

            List<KeyValuePair<string, int>> topLevelDirectoriesByHydration = topLevelDirectoryHydrationLookup.ToList();
            topLevelDirectoriesByHydration.Sort(
                delegate(KeyValuePair<string, int> pairOne, KeyValuePair<string, int> pairTwo)
                {
                    return pairOne.Value.CompareTo(pairTwo.Value);
                });

            this.Output.WriteLine("\nRepository statistics");
            this.Output.WriteLine("Total paths tracked by git:     " + trackedFilesCountFormatted.PadLeft(longest) + " | 100%");
            this.Output.WriteLine("Total number of placeholders:   " + placeholderCountFormatted.PadLeft(longest) + " | " + this.FormattedPercent(((double)placeholderCount) / trackedFilesCount));
            this.Output.WriteLine("Total number of modified paths: " + modifiedPathsCountFormatted.PadLeft(longest) + " | " + this.FormattedPercent(((double)modifiedPathsCount) / trackedFilesCount));

            this.Output.WriteLine("\nTotal hydration percentage:     " + this.FormattedPercent((double)(placeholderCount + modifiedPathsCount) / trackedFilesCount).PadLeft(longest + 7));

            this.Output.WriteLine("\nMost hydrated top level directories:");

            int maxDirectoryNameLength = 0;
            for (int i = 0; i < 5 && i < topLevelDirectoriesByHydration.Count; i++)
            {
                maxDirectoryNameLength = Math.Max(maxDirectoryNameLength, topLevelDirectoriesByHydration[i].Key.Length);
            }

            for (int i = 0; i < 5 && i < topLevelDirectoriesByHydration.Count; i++)
            {
                string dir = topLevelDirectoriesByHydration[i].Key.PadLeft(maxDirectoryNameLength);
                string percent = this.FormattedPercent((double)topLevelDirectoriesByHydration[i].Value / trackedFilesLookup[topLevelDirectoriesByHydration[i].Key]);

                this.Output.WriteLine($"{dir} | {topLevelDirectoriesByHydration[i].Value} / {trackedFilesLookup[topLevelDirectoriesByHydration[i].Key]} = {percent}");

                // this.Output.WriteLine(" " + percent + " | " + dir + " | Primarily hydrated by ____________");
            }

            Console.ReadLine();
        }

        private string FormattedPercent(double percent)
        {
            return percent.ToString("P0").PadLeft(4);
        }

        private string TrimGitIndexLine(string line)
        {
            return line.Substring(line.IndexOf("blob") + 46);
        }

        private string TopDirectory(string path)
        {
            int whackLocation = path.IndexOf('/');
            if (whackLocation == -1)
            {
                return "/";
            }

            return path.Substring(0, whackLocation);
        }
    }
}