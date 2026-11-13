using GVFS.Common;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.Platform.Windows
{
    public static class DevDriveFilterPolicy
    {
        private const string VolumeHeader = "Filters allowed on this developer volume:";
        private const string MachineHeader = "Filters allowed on any developer volume:";
        private const string GroupPolicyHeader = "Filters allowed on any developer volume, by group policy:";
        private const string AttachedHeader = "Filters currently attached to this developer volume:";
        private static readonly Regex FilterNamePattern = new Regex(@"\A[A-Za-z0-9._-]+\z", RegexOptions.Compiled);
        private static readonly TimeSpan UpdateTimeout = TimeSpan.FromSeconds(30);
        private static readonly Lock PolicyLock = new Lock();
        private static volatile bool processCleanupFailed;

        public enum AllowFilterResult
        {
            AlreadyAllowed,
            Added,
            RemountRequired,
            PolicyUnreadable,
            Failed,
        }

        public static AllowFilterResult TryAllowFilter(string volumeRoot, ITracer tracer, out string error)
        {
            if (!PolicyLock.TryEnter(UpdateTimeout))
            {
                error = "Another Dev Drive policy update is in progress. Retry the mount.";
                tracer.RelatedWarning(error);
                return AllowFilterResult.Failed;
            }

            try
            {
                if (processCleanupFailed)
                {
                    error = "Automatic Dev Drive updates are disabled because a previous policy process did not stop. Restart GVFS.Service after that process exits.";
                    tracer.RelatedWarning(error);
                    return AllowFilterResult.Failed;
                }

                Stopwatch elapsed = Stopwatch.StartNew();
                return TryAllowFilter(
                    volumeRoot,
                    arguments => RunFsUtil(arguments, UpdateTimeout - elapsed.Elapsed, tracer),
                    tracer,
                    out error);
            }
            catch (Exception e) when (e is Win32Exception || e is IOException || e is UnauthorizedAccessException || e is AggregateException)
            {
                error = $"Failed to update the Dev Drive filter allowance. The saved policy can already include the change. {e.Message}";
                tracer.RelatedWarning($"{nameof(TryAllowFilter)}: {e}");
                return AllowFilterResult.Failed;
            }
            finally
            {
                PolicyLock.Exit();
            }
        }

        internal static AllowFilterResult TryAllowFilter(
            string volumeRoot,
            Func<string, ProcessResult> runFsUtil,
            ITracer tracer,
            out string error)
        {
            if (!TryQueryPolicy(volumeRoot, runFsUtil, tracer, out Allowances before, out error))
            {
                return AllowFilterResult.PolicyUnreadable;
            }

            if (before.AllowsProjFS)
            {
                return AllowFilterResult.AlreadyAllowed;
            }

            // Recheck before replacement. External administrators do not share PolicyLock,
            // so this detects an observed change but cannot provide compare-and-swap.
            if (!TryQueryPolicy(volumeRoot, runFsUtil, tracer, out Allowances current, out error))
            {
                return AllowFilterResult.PolicyUnreadable;
            }

            if (current.AllowsProjFS)
            {
                return AllowFilterResult.AlreadyAllowed;
            }

            if (!new HashSet<string>(before.Volume, StringComparer.OrdinalIgnoreCase).SetEquals(current.Volume))
            {
                error = "The volume filter allowance changed during the request. No update was attempted. Retry the mount.";
                tracer.RelatedWarning(error);
                return AllowFilterResult.Failed;
            }

            List<string> updated = new List<string>(current.Volume) { ProjFSFilter.ServiceName };
            EventMetadata audit = new EventMetadata
            {
                { "VolumeRoot", volumeRoot },
                { "PreviousVolumeFilters", string.Join(",", current.Volume) },
                { "RequestedVolumeFilters", string.Join(",", updated) },
            };
            tracer.RelatedEvent(EventLevel.Informational, "DevDriveFilterPolicy_UpdateAttempt", audit, Keywords.Telemetry);

            string volumeArgument = CommandLineEscaping.EscapeArgument(volumeRoot);
            ProcessResult write = runFsUtil($"devdrv setfiltersallowed /volume {volumeArgument} \"{string.Join(",", updated)}\"");
            audit.Add("ExitCode", write.ExitCode);
            tracer.RelatedEvent(EventLevel.Informational, "DevDriveFilterPolicy_UpdateResult", audit, Keywords.Telemetry);

            // On a busy volume fsutil can persist the allowance but fail to dismount.
            // Read back even after a nonzero exit; do not report an unverified write as success.
            if (!TryQueryPolicy(volumeRoot, runFsUtil, tracer, out Allowances verified, out string verifyError))
            {
                error = $"The volume allowance update was attempted, but verification failed. {verifyError} {write.Output} {write.Errors}".Trim();
                tracer.RelatedWarning(error);
                return AllowFilterResult.Failed;
            }

            List<string> missing = updated.Except(verified.Volume, StringComparer.OrdinalIgnoreCase).ToList();
            if (missing.Count != 0)
            {
                error = $"The saved volume allowance does not contain these expected filters: {string.Join(",", missing)}. {write.Output} {write.Errors}".Trim();
                tracer.RelatedWarning(error);
                return AllowFilterResult.Failed;
            }

            audit.Add("SavedVolumeFilters", string.Join(",", verified.Volume));
            tracer.RelatedEvent(EventLevel.Informational, "DevDriveFilterPolicy_UpdateVerified", audit, Keywords.Telemetry);
            error = null;
            if (write.ExitCode != 0)
            {
                tracer.RelatedWarning($"The volume allowance persists, but fsutil reported exit {write.ExitCode}: {write.Output} {write.Errors}");
                return AllowFilterResult.RemountRequired;
            }

            return AllowFilterResult.Added;
        }

        internal static bool TryParseAllowedFilters(string queryOutput, out Allowances allowances)
        {
            allowances = null;
            if (string.IsNullOrWhiteSpace(queryOutput))
            {
                return false;
            }

            Dictionary<string, List<string>> sections = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            List<string> currentSection = null;
            bool developerVolume = false;
            foreach (string rawLine in queryOutput.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0)
                {
                    currentSection = null;
                    continue;
                }

                if (line == VolumeHeader || line == MachineHeader || line == GroupPolicyHeader || line == AttachedHeader)
                {
                    if (sections.ContainsKey(line))
                    {
                        return false;
                    }

                    currentSection = new List<string>();
                    sections.Add(line, currentSection);
                }
                else if (currentSection != null)
                {
                    string filterLine = line.EndsWith(",", StringComparison.Ordinal) ? line.Substring(0, line.Length - 1) : line;
                    foreach (string token in filterLine.Split(','))
                    {
                        string name = token.Trim();
                        if (!FilterNamePattern.IsMatch(name))
                        {
                            return false;
                        }

                        currentSection.Add(name);
                    }
                }
                else if (line == "This is a trusted developer volume." || line == "This is an untrusted developer volume.")
                {
                    developerVolume = true;
                }
                else if (line != "Developer volumes are protected by antivirus filter, by group policy."
                    && line != "Developer volumes are protected by antivirus filter.")
                {
                    return false;
                }
            }

            if (!developerVolume || sections.Any(section => section.Key != AttachedHeader && section.Value.Count == 0))
            {
                return false;
            }

            allowances = new Allowances(
                sections.TryGetValue(VolumeHeader, out List<string> volume) ? volume : new List<string>(),
                sections.Where(section => section.Key == MachineHeader || section.Key == GroupPolicyHeader).SelectMany(section => section.Value).ToList());
            return true;
        }

        private static bool TryQueryPolicy(
            string volumeRoot,
            Func<string, ProcessResult> runFsUtil,
            ITracer tracer,
            out Allowances allowances,
            out string error)
        {
            allowances = null;
            ProcessResult result = runFsUtil("devdrv query " + CommandLineEscaping.EscapeArgument(volumeRoot));
            if (result.ExitCode != 0)
            {
                error = $"The Dev Drive query failed with exit {result.ExitCode}: {result.Output} {result.Errors}".Trim();
            }
            else if (!TryParseAllowedFilters(result.Output, out allowances))
            {
                error = "The Dev Drive query output was not recognized. No further policy changes were made.";
            }
            else
            {
                error = null;
                return true;
            }

            tracer.RelatedWarning(error);
            return false;
        }

        private static ProcessResult RunFsUtil(string arguments, TimeSpan timeout, ITracer tracer)
        {
            return RunProcess(
                new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "fsutil.exe"), arguments)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
                timeout,
                tracer);
        }

        internal static ProcessResult RunProcess(ProcessStartInfo startInfo, TimeSpan timeout, ITracer tracer)
        {
            if (timeout <= TimeSpan.Zero)
            {
                return new ProcessResult(string.Empty, "The Dev Drive policy update exceeded its 30-second budget.", -1);
            }

            using (Process process = new Process())
            using (CancellationTokenSource deadline = new CancellationTokenSource(timeout))
            {
                process.StartInfo = startInfo;
                process.Start();
                using (deadline.Token.Register(() =>
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch (InvalidOperationException)
                    {
                        // The child exited between cancellation and termination.
                    }
                    catch (Exception e) when (e is Win32Exception || e is AggregateException)
                    {
                        tracer.RelatedWarning($"Failed to stop the timed-out fsutil process: {e}");
                    }
                }))
                {
                    try
                    {
                        Task<string> output = process.StandardOutput.ReadToEndAsync(deadline.Token);
                        Task<string> errors = process.StandardError.ReadToEndAsync(deadline.Token);
                        Task.WhenAll(output, errors, process.WaitForExitAsync(deadline.Token)).GetAwaiter().GetResult();
                        return new ProcessResult(output.Result, errors.Result, process.ExitCode);
                    }
                    catch (OperationCanceledException)
                    {
                        return new ProcessResult(string.Empty, "The Dev Drive policy update timed out. The saved allowance can already include the change.", -1);
                    }
                    finally
                    {
                        // Cancellation can finish the waits before its termination callback runs.
                        // Stop and observe the child before disposal can unregister that callback.
                        try
                        {
                            if (!process.HasExited)
                            {
                                try
                                {
                                    process.Kill(entireProcessTree: true);
                                }
                                catch (InvalidOperationException)
                                {
                                    // The child exited between the state check and termination.
                                }

                                if (!process.WaitForExit(5000))
                                {
                                    throw new Win32Exception("The policy process did not stop within the cleanup deadline.");
                                }
                            }
                        }
                        catch (Exception e) when (e is Win32Exception || e is AggregateException)
                        {
                            processCleanupFailed = true;
                            tracer.RelatedWarning($"Further Dev Drive policy updates are disabled because child cleanup failed: {e}");
                            throw;
                        }
                    }
                }
            }
        }

        internal sealed class Allowances
        {
            public Allowances(List<string> volume, List<string> inherited)
            {
                this.Volume = volume;
                this.Inherited = inherited;
            }

            public List<string> Volume { get; }
            public List<string> Inherited { get; }
            public bool AllowsProjFS => this.Volume.Concat(this.Inherited).Contains(ProjFSFilter.ServiceName, StringComparer.OrdinalIgnoreCase);
        }
    }
}
