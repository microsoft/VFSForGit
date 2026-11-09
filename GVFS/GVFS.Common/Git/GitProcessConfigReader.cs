using GVFS.Common.Tracing;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.Common.Git
{
    internal static class GitProcessConfigReader
    {
        private const int MaxOutput = 65536;
        private const int MaxErrors = 4096;

        internal static GitProcess.Result Read(
            string configName,
            Func<GitProcess.Result> readConfig,
            ITracer tracer,
            Action<TimeSpan> delay)
        {
            return GitConfigReadRetry.InvokeResult(
                configName,
                readConfig,
                result => result.ExitCodeIsFailure &&
                    !result.OutputTruncated && !result.ErrorsTruncated &&
                    GitConfigReadRetry.IsTransientFileAccessError(result.Errors, lineMustContain: "unable to access"),
                tracer,
                delay);
        }

        // The factory must return a fresh, unstarted process for each attempt.
        // The caller's token covers all attempts and retry delays.
        internal static GitProcess.Result ReadRaw(
            string configName,
            Func<Process> createProcess,
            string input,
            CancellationToken cancellation,
            ITracer tracer)
        {
            return Read(
                configName,
                () =>
                {
                    cancellation.ThrowIfCancellationRequested();
                    using (Process process = createProcess())
                    {
                        return RunAsync(process, input, cancellation).GetAwaiter().GetResult();
                    }
                },
                tracer ?? NullTracer.Instance,
                delay => Task.Delay(delay, cancellation).GetAwaiter().GetResult());
        }

        private static async Task<GitProcess.Result> RunAsync(Process process, string input, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            // Config output and URL-bearing command arguments must not enter inherited Git traces.
            foreach (string key in process.StartInfo.Environment.Keys.Where(k => k.StartsWith("GIT_TRACE", StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                process.StartInfo.Environment.Remove(key);
            }

            process.StartInfo.Environment["GIT_TRACE2"] = "0";
            process.StartInfo.Environment["GIT_TRACE2_EVENT"] = "0";
            process.StartInfo.Environment["GIT_TRACE2_PERF"] = "0";
            process.StartInfo.Environment["LC_ALL"] = "C";
            process.Start();
            int killFailed = 0;
            using (CancellationTokenRegistration kill = cancellation.Register(() =>
            {
                if (!TryKillProcess(() => process.Kill(entireProcessTree: true)))
                {
                    Interlocked.Exchange(ref killFailed, 1);
                }
            }))
            {
                try
                {
                    Task<(string Text, bool Truncated)> stdout = ReadBoundedAsync(process.StandardOutput, MaxOutput, cancellation);
                    Task<(string Text, bool Truncated)> stderr = ReadBoundedAsync(process.StandardError, MaxErrors, cancellation);
                    Task write = WriteInputAsync(process, input, cancellation);
                    await Task.WhenAll(stdout, stderr, write, process.WaitForExitAsync(cancellation));
                    (string Text, bool Truncated) output = await stdout;
                    (string Text, bool Truncated) errors = await stderr;
                    return new GitProcess.Result(output.Text, errors.Text, process.ExitCode, output.Truncated, errors.Truncated);
                }
                finally
                {
                    if (!process.HasExited)
                    {
                        if (!TryKillProcess(() => process.Kill(entireProcessTree: true)))
                        {
                            Interlocked.Exchange(ref killFailed, 1);
                        }
                    }

                    if (killFailed != 0)
                    {
                        throw new Win32Exception();
                    }
                }
            }
        }

        internal static bool TryKillProcess(Action kill)
        {
            try
            {
                kill();
                return true;
            }
            catch (InvalidOperationException)
            {
                // The query already exited.
                return true;
            }
            catch (Win32Exception)
            {
                return false;
            }
            catch (AggregateException)
            {
                return false;
            }
        }

        private static async Task WriteInputAsync(Process process, string input, CancellationToken cancellation)
        {
            await process.StandardInput.WriteAsync(input.AsMemory(), cancellation);
            process.StandardInput.Close();
        }

        private static async Task<(string Text, bool Truncated)> ReadBoundedAsync(StreamReader reader, int limit, CancellationToken cancellation)
        {
            StringBuilder text = new StringBuilder();
            char[] buffer = new char[4096];
            bool truncated = false;
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellation)) != 0)
            {
                int retained = Math.Min(count, limit - text.Length);
                text.Append(buffer, 0, retained);
                truncated |= retained < count;
            }

            return (text.ToString(), truncated);
        }
    }
}
