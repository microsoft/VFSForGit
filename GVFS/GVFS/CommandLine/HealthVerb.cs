using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.CommandLine
{
    public class HealthVerb : GVFSVerb.ForExistingEnlistment
    {
        private const string HealthVerbName = "health";
        private const decimal MaximumHealthyHydration = 0.5m;

        public int DirectoryDisplayCount { get; set; } = 5;

        public string Directory { get; set; }

        public bool StatusOnly { get; set; }

        public static System.CommandLine.Command CreateCommand()
        {
            System.CommandLine.Command cmd = new System.CommandLine.Command("health", "EXPERIMENTAL FEATURE - Measure the health of the repository");

            System.CommandLine.Argument<string> enlistmentArg = GVFSVerb.CreateEnlistmentPathArgument();
            cmd.Add(enlistmentArg);

            System.CommandLine.Option<int> displayCountOption = new System.CommandLine.Option<int>("-n")
            {
                Description = "Only display the <n> most hydrated directories in the output",
                DefaultValueFactory = (_) => 5,
            };
            cmd.Add(displayCountOption);

            System.CommandLine.Option<string> directoryOption = new System.CommandLine.Option<string>("--directory", new[] { "-d" }) { Description = "Get the health of a specific directory (default is the current working directory)" };
            cmd.Add(directoryOption);

            System.CommandLine.Option<bool> statusOption = new System.CommandLine.Option<bool>("--status", new[] { "-s" }) { Description = "Display only the hydration % of the repository, similar to 'git status' in a repository with sparse-checkout" };
            cmd.Add(statusOption);

            System.CommandLine.Option<string> internalOption = GVFSVerb.CreateInternalParametersOption();
            cmd.Add(internalOption);

            GVFSVerb.SetActionForVerbWithEnlistment<HealthVerb>(cmd, enlistmentArg, internalOption, defaultEnlistmentPathToCwd: true,
                (verb, result) =>
                {
                    verb.DirectoryDisplayCount = result.GetValue(displayCountOption);
                    verb.Directory = result.GetValue(directoryOption);
                    verb.StatusOnly = result.GetValue(statusOption);
                });

            return cmd;
        }

        protected override string VerbName => HealthVerbName;

        internal PhysicalFileSystem FileSystem { get; set; } = new PhysicalFileSystem();

        protected override void Execute(GVFSEnlistment enlistment)
        {
            using (JsonTracer tracer = new JsonTracer(GVFSConstants.GVFSEtwProviderName, HealthVerbName))
            {
                tracer.AddLogFileEventListener(
                    GVFSEnlistment.GetNewGVFSLogFileName(enlistment.GVFSLogsRoot, GVFSConstants.LogFileTypes.Health),
                    EventLevel.Informational,
                    Keywords.Any);

                Guid commandId = Guid.NewGuid();
                this.ExecuteWithCompletion(enlistment, tracer, commandId);
            }
        }

        internal void ExecuteWithCompletion(GVFSEnlistment enlistment, ITracer tracer, Guid commandId)
        {
            EventMetadata metadata = new EventMetadata
            {
                ["SchemaVersion"] = 1,
                ["CommandId"] = commandId.ToString("N"),
                ["Mode"] = this.StatusOnly ? "Status" : "Detailed",
                ["Outcome"] = HealthCommandOutcome.Failed,
            };
            try
            {
                this.ExecuteWithTracer(enlistment, tracer, metadata, commandId);
            }
            catch (Exception e)
            {
                metadata["Outcome"] = HealthCommandOutcome.Failed;
                metadata["ExceptionType"] = e.GetType().FullName;
                metadata["ExceptionHResult"] = e.HResult;
                tracer.RelatedWarning(new EventMetadata { ["Exception"] = e.ToString() }, "Health command failed.", Keywords.None);
                throw;
            }
            finally
            {
                tracer.Stop(metadata);
            }
        }

        internal virtual void ExecuteWithTracer(GVFSEnlistment enlistment, ITracer tracer, EventMetadata metadata, Guid commandId)
        {
            if (this.StatusOnly)
            {
                this.OutputHydrationPercent(enlistment, tracer, metadata, commandId);
                return;
            }

            // Now default to the current working directory when running the verb without a specified path
            if (string.IsNullOrEmpty(this.Directory) || this.Directory.Equals("."))
            {
                if (Environment.CurrentDirectory.StartsWith(enlistment.WorkingDirectoryRoot, GVFSPlatform.Instance.Constants.PathComparison))
                {
                    this.Directory = Environment.CurrentDirectory.Substring(enlistment.WorkingDirectoryRoot.Length);
                }
                else
                {
                    // If the path is not under the source root, set the directory to empty
                    this.Directory = string.Empty;
                }
            }

            this.Output.WriteLine("\nGathering repository data...");

            this.Directory = this.Directory.Replace(GVFSPlatform.GVFSPlatformConstants.PathSeparator, GVFSConstants.GitPathSeparator);

            EnlistmentPathData pathData = new EnlistmentPathData();

            pathData.LoadPlaceholdersFromDatabase(enlistment);
            pathData.LoadModifiedPaths(enlistment, tracer);
            pathData.LoadPathsFromGitIndex(enlistment);

            pathData.NormalizeAllPaths();

            EnlistmentHealthCalculator enlistmentHealthCalculator = new EnlistmentHealthCalculator(pathData);
            EnlistmentHealthData enlistmentHealthData = enlistmentHealthCalculator.CalculateStatistics(this.Directory);

            this.PrintOutput(enlistmentHealthData);
            metadata["Outcome"] = HealthCommandOutcome.Success;
        }

        internal void OutputHydrationPercent(GVFSEnlistment enlistment, ITracer tracer, EventMetadata metadata, Guid commandId)
        {
            CacheLookupResult cache = this.TryGetCachedHydrationStatus(enlistment);
            metadata["Cache"] = cache.GetMetadata();
            if (cache.Exception != null)
            {
                tracer.RelatedWarning(new EventMetadata { ["Exception"] = cache.Exception.ToString() }, "Hydration cache lookup failed.", Keywords.None);
            }

            if (cache.Response != null && cache.Outcome == CacheLookupOutcome.Success)
            {
                metadata["ResultSource"] = "Cache";
                metadata["DisplayOutcome"] = "Percentage";
                metadata["Outcome"] = HealthCommandOutcome.Success;
                metadata["CalculationOutcome"] = cache.Info == null ? null
                    : cache.Info.CountsComplete ? HydrationSummaryOutcome.Success : HydrationSummaryOutcome.IncompleteData;
                metadata["CalculationId"] = cache.Info?.CalculationId?.ToString("N");
                this.Output.WriteLine(cache.Response.ToDisplayMessage());
                return;
            }

            metadata["ResultSource"] = "Fallback";
            HydrationSummaryDiagnostics diagnostics = new HydrationSummaryDiagnostics
            {
                CommandId = commandId,
                Caller = HydrationSummaryCaller.HealthFallback,
                FolderCountSource = HydrationFolderCountSource.IndexParser,
            };
            EnlistmentHydrationSummary summary = this.CreateHydrationSummary(enlistment, tracer, diagnostics);
            metadata["Summary"] = diagnostics.GetMetadata();
            metadata["CalculationId"] = diagnostics.CalculationId.ToString("N");
            metadata["Outcome"] = summary.IsValid ? HealthCommandOutcome.Success : HealthCommandOutcome.Failed;
            metadata["CalculationOutcome"] = diagnostics.Outcome;
            metadata["DisplayOutcome"] = summary.IsValid ? "Percentage" : "Error";
            this.Output.WriteLine(summary.ToMessage());
        }

        internal virtual EnlistmentHydrationSummary CreateHydrationSummary(GVFSEnlistment enlistment, ITracer tracer, HydrationSummaryDiagnostics diagnostics)
        {
            return EnlistmentHydrationSummary.CreateSummary(enlistment, this.FileSystem, tracer,
                () => GVFS.Virtualization.Projection.GitIndexProjection.CountIndexFolders(tracer, enlistment.GitIndexPath),
                diagnostics: diagnostics);
        }

        internal virtual CacheLookupResult TryGetCachedHydrationStatus(GVFSEnlistment enlistment)
        {
            const int ConnectTimeoutMs = 500;
            const int TotalTimeoutMs = 1000;
            Stopwatch stopwatch = Stopwatch.StartNew();
            int phase = (int)CacheLookupPhase.Connect;
            Task<CacheLookupResult> task = Task.Run(() =>
            {
                try
                {
                    using (NamedPipeClient pipeClient = this.CreateHydrationPipeClient(enlistment))
                    {
                        if (!pipeClient.Connect(ConnectTimeoutMs, out Exception error))
                        {
                            return new CacheLookupResult
                            {
                                Outcome = error is TimeoutException ? CacheLookupOutcome.ConnectTimeout : CacheLookupOutcome.ConnectFailed,
                                Phase = CacheLookupPhase.Connect,
                                Exception = error,
                            };
                        }

                        Volatile.Write(ref phase, (int)CacheLookupPhase.Send);
                        pipeClient.SendRequest(new NamedPipeMessages.Message(NamedPipeMessages.HydrationStatus.Request, null));
                        Volatile.Write(ref phase, (int)CacheLookupPhase.Read);
                        NamedPipeMessages.Message response = pipeClient.ReadResponse();
                        Volatile.Write(ref phase, (int)CacheLookupPhase.Parse);
                        return ParseCachedResponse(response);
                    }
                }
                catch (Exception ex)
                {
                    return new CacheLookupResult { Outcome = CacheLookupOutcome.Failed, Phase = (CacheLookupPhase)Volatile.Read(ref phase), Exception = ex };
                }
            });

            CacheLookupResult result;
            if (task.Wait(TotalTimeoutMs))
            {
                result = task.Result;
            }
            else
            {
                // The worker owns the pipe and never writes to the caller's tracer or metadata after a timeout.
                result = new CacheLookupResult { Outcome = CacheLookupOutcome.Timeout, Phase = (CacheLookupPhase)Volatile.Read(ref phase) };
            }

            result.DurationMs = stopwatch.ElapsedMilliseconds;
            return result;
        }

        internal virtual NamedPipeClient CreateHydrationPipeClient(GVFSEnlistment enlistment)
        {
            return new NamedPipeClient(enlistment.NamedPipeName);
        }

        internal enum HealthCommandOutcome
        {
            Success,
            Failed,
        }

        internal static CacheLookupResult ParseCachedResponse(NamedPipeMessages.Message message)
        {
            CacheLookupResult result = new CacheLookupResult { Phase = CacheLookupPhase.Parse };
            if (message.Header == NamedPipeMessages.HydrationStatus.NotAvailableResult)
            {
                result.Outcome = CacheLookupOutcome.NotAvailable;
                NamedPipeMessages.HydrationStatus.CacheInfo.TryParse(message.Body, out NamedPipeMessages.HydrationStatus.CacheInfo info);
                result.Info = info;
                result.DiagnosticsStatus = info != null ? CacheDiagnosticsStatus.Parsed
                    : string.IsNullOrEmpty(message.Body) ? CacheDiagnosticsStatus.Absent : CacheDiagnosticsStatus.Unrecognized;
                return result;
            }

            if (message.Header != NamedPipeMessages.HydrationStatus.SuccessResult)
            {
                result.Outcome = CacheLookupOutcome.UnexpectedResponse;
                return result;
            }

            result.Counts = new EventMetadata();
            string[] parts = message.Body?.Split(',') ?? Array.Empty<string>();
            string[] names = { "PlaceholderFileCount", "PlaceholderFolderCount", "ModifiedFileCount", "ModifiedFolderCount", "TotalFileCount", "TotalFolderCount" };
            for (int i = 0; i < names.Length; i++)
            {
                result.Counts[names[i]] = i < parts.Length && int.TryParse(parts[i], out int count) ? count : null;
            }

            bool valid = NamedPipeMessages.HydrationStatus.Response.TryParse(message.Body, out NamedPipeMessages.HydrationStatus.Response response, out NamedPipeMessages.HydrationStatus.ParseOutcome outcome);
            result.Response = response;
            result.ParseOutcome = outcome;
            result.Outcome = valid ? CacheLookupOutcome.Success
                : outcome == NamedPipeMessages.HydrationStatus.ParseOutcome.IncompleteData ? CacheLookupOutcome.IncompleteData
                : outcome == NamedPipeMessages.HydrationStatus.ParseOutcome.InvalidCounts ? CacheLookupOutcome.InvalidCounts : CacheLookupOutcome.MalformedResponse;
            NamedPipeMessages.HydrationStatus.CacheInfo.TryParse(message.Body, out NamedPipeMessages.HydrationStatus.CacheInfo cacheInfo, hasCounts: true);
            result.Info = cacheInfo;
            result.DiagnosticsStatus = cacheInfo != null ? CacheDiagnosticsStatus.Parsed
                : parts.Length <= 6 ? CacheDiagnosticsStatus.Absent : CacheDiagnosticsStatus.Unrecognized;
            return result;
        }

        internal enum CacheDiagnosticsStatus
        {
            Absent,
            Parsed,
            Unrecognized,
        }

        internal enum CacheLookupOutcome
        {
            Success,
            NotAvailable,
            ConnectTimeout,
            ConnectFailed,
            Timeout,
            UnexpectedResponse,
            MalformedResponse,
            InvalidCounts,
            IncompleteData,
            Failed,
        }

        internal enum CacheLookupPhase
        {
            Connect,
            Send,
            Read,
            Parse,
        }

        internal sealed class CacheLookupResult
        {
            public CacheLookupOutcome Outcome { get; set; }
            public CacheLookupPhase Phase { get; set; }
            public long DurationMs { get; set; }
            public Exception Exception { get; set; }
            public NamedPipeMessages.HydrationStatus.Response Response { get; set; }
            public NamedPipeMessages.HydrationStatus.ParseOutcome? ParseOutcome { get; set; }
            public NamedPipeMessages.HydrationStatus.CacheInfo Info { get; set; }
            public EventMetadata Counts { get; set; }
            public CacheDiagnosticsStatus DiagnosticsStatus { get; set; }

            public EventMetadata GetMetadata()
            {
                return new EventMetadata
                {
                    [nameof(this.Outcome)] = this.Outcome,
                    [nameof(this.Phase)] = this.Phase,
                    [nameof(this.DurationMs)] = this.DurationMs,
                    ["ExceptionType"] = this.Exception?.GetType().FullName,
                    ["ExceptionHResult"] = this.Exception?.HResult,
                    [nameof(this.ParseOutcome)] = this.ParseOutcome,
                    [nameof(this.Counts)] = this.Counts,
                    ["FileCountSource"] = this.Response == null ? null : "IndexHeader",
                    ["FolderCountSource"] = this.Response == null ? null : HydrationFolderCountSource.Projection,
                    ["FileCountDelta"] = this.Response?.FileCountDelta,
                    ["FolderCountDelta"] = this.Response?.FolderCountDelta,
                    ["FileCountExcess"] = this.Response?.FileCountExcess,
                    ["FolderCountExcess"] = this.Response?.FolderCountExcess,
                    ["DiagnosticsAvailable"] = this.Info != null,
                    [nameof(this.DiagnosticsStatus)] = this.DiagnosticsStatus,
                    ["State"] = this.Info?.State,
                    ["CalculationId"] = this.Info?.CalculationId?.ToString("N"),
                    ["AgeMs"] = this.Info?.AgeMs,
                    ["Invalidated"] = this.Info?.Invalidated,
                    ["CountsComplete"] = this.Info?.CountsComplete,
                };
            }
        }

        private void PrintOutput(EnlistmentHealthData enlistmentHealthData)
        {
            string trackedFilesCountFormatted = enlistmentHealthData.GitTrackedItemsCount.ToString("N0");
            string placeholderCountFormatted = enlistmentHealthData.PlaceholderCount.ToString("N0");
            string modifiedPathsCountFormatted = enlistmentHealthData.ModifiedPathsCount.ToString("N0");

            // Calculate spacing for the numbers of total files
            int longest = Math.Max(trackedFilesCountFormatted.Length, placeholderCountFormatted.Length);
            longest = Math.Max(longest, modifiedPathsCountFormatted.Length);

            // Sort the dictionary to find the most hydrated directories by health score
            List<EnlistmentHealthCalculator.SubDirectoryInfo> topLevelDirectoriesByHydration = enlistmentHealthData.DirectoryHydrationLevels.Take(this.DirectoryDisplayCount).ToList();

            this.Output.WriteLine("\nHealth of directory: " + enlistmentHealthData.TargetDirectory);
            this.Output.WriteLine("Total files in HEAD commit:           " + trackedFilesCountFormatted.PadLeft(longest) + " | 100%");
            this.Output.WriteLine("Files managed by VFS for Git (fast):  " + placeholderCountFormatted.PadLeft(longest) + " | " + this.FormatPercent(enlistmentHealthData.PlaceholderPercentage));
            this.Output.WriteLine("Files managed by Git:                 " + modifiedPathsCountFormatted.PadLeft(longest) + " | " + this.FormatPercent(enlistmentHealthData.ModifiedPathsPercentage));

            this.Output.WriteLine("\nTotal hydration percentage:           " + this.FormatPercent(enlistmentHealthData.PlaceholderPercentage + enlistmentHealthData.ModifiedPathsPercentage).PadLeft(longest + 7));

            this.Output.WriteLine("\nMost hydrated top level directories:");

            int maxCountLength = 0;
            int maxTotalLength = 0;
            foreach (EnlistmentHealthCalculator.SubDirectoryInfo directoryInfo in topLevelDirectoriesByHydration)
            {
                maxCountLength = Math.Max(maxCountLength, directoryInfo.HydratedFileCount.ToString("N0").Length);
                maxTotalLength = Math.Max(maxTotalLength, directoryInfo.TotalFileCount.ToString("N0").Length);
            }

            foreach (EnlistmentHealthCalculator.SubDirectoryInfo directoryInfo in topLevelDirectoriesByHydration)
            {
                this.Output.WriteLine(" " + directoryInfo.HydratedFileCount.ToString("N0").PadLeft(maxCountLength) + " / " + directoryInfo.TotalFileCount.ToString("N0").PadRight(maxTotalLength) + " | " + directoryInfo.Name);
            }

            bool healthyRepo = (enlistmentHealthData.PlaceholderPercentage + enlistmentHealthData.ModifiedPathsPercentage) < MaximumHealthyHydration;

            // Only label the summary as "Repository status" when the calculation actually covered
            // the whole enlistment. When the user scoped the check to a subdirectory (via -d or by
            // running from a subdirectory of the enlistment) the number describes only that subtree,
            // so label it accordingly to avoid falsely reporting the repository as highly hydrated.
            bool isWholeRepo = string.IsNullOrEmpty(enlistmentHealthData.TargetDirectory)
                || enlistmentHealthData.TargetDirectory == GVFSConstants.GitPathSeparatorString;

            string statusLabel = isWholeRepo
                ? "Repository status"
                : "Directory status (" + enlistmentHealthData.TargetDirectory.TrimEnd(GVFSConstants.GitPathSeparator) + ")";

            this.Output.WriteLine("\n" + statusLabel + ": " + (healthyRepo ? "OK" : "Highly Hydrated"));

            if (!isWholeRepo)
            {
                this.Output.WriteLine("To see the full repository status, switch to the root of the repository and re-run 'gvfs health'.");
            }
        }

        /// <summary>
        /// Takes a fractional decimal and formats it as a percent taking exactly 4 characters with no decimals
        /// </summary>
        /// <param name="percent">Fractional decimal to format to a percent</param>
        /// <returns>A 4 character string formatting the percent correctly</returns>
        private string FormatPercent(decimal percent)
        {
            return percent.ToString("P0").PadLeft(4);
        }
    }
}
