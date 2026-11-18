using GVFS.Common.Tracing;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.Common.Git
{
    internal static class GitProcessConfigReader
    {
        private const int MaxOutput = 65536;
        private const int MaxErrors = 4096;

        // The caller owns process execution. This method retries only Git results.
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
        // This method reports native process failures as GitConfigProcessException.
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
                    try
                    {
                        using (Process process = createProcess())
                        {
                            return RunAsync(process, input, cancellation).GetAwaiter().GetResult();
                        }
                    }
                    catch (Exception ex) when (IsNativeProcessError(ex))
                    {
                        throw new GitConfigProcessException(ex);
                    }
                },
                tracer ?? NullTracer.Instance,
                delay => Task.Delay(delay, cancellation).GetAwaiter().GetResult());
        }

        private static Task<GitProcess.Result> RunAsync(Process process, string input, CancellationToken cancellation)
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
            return ReadWithProcessCleanupAsync(
                ReadResultAsync,
                () => process.HasExited,
                () => process.Kill(entireProcessTree: true),
                cancellation);

            async Task<GitProcess.Result> ReadResultAsync()
            {
                Task<(string Text, bool Truncated)> stdout = ReadBoundedAsync(process.StandardOutput, MaxOutput, cancellation);
                Task<(string Text, bool Truncated)> stderr = ReadBoundedAsync(process.StandardError, MaxErrors, cancellation);
                Task write = WriteInputAsync(process, input, cancellation);
                Task exited = process.WaitForExitAsync(cancellation);
                try
                {
                    await Task.WhenAll(stdout, stderr, write, exited);
                }
                catch (IOException) when (
                    write.IsFaulted && stdout.IsCompletedSuccessfully && stderr.IsCompletedSuccessfully &&
                    exited.IsCompletedSuccessfully && process.ExitCode != 0)
                {
                    // Git can reject the query before reading stdin. Preserve its error
                    // instead of replacing it with the resulting broken-pipe exception.
                    cancellation.ThrowIfCancellationRequested();
                }

                (string Text, bool Truncated) output = await stdout;
                (string Text, bool Truncated) errors = await stderr;
                return new GitProcess.Result(output.Text, errors.Text, process.ExitCode, output.Truncated, errors.Truncated);
            }
        }

        internal static async Task<GitProcess.Result> ReadWithProcessCleanupAsync(
            Func<Task<GitProcess.Result>> read,
            Func<bool> hasExited,
            Action killTree,
            CancellationToken cancellation)
        {
            TaskCompletionSource<Exception> cancellationKill = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationTokenRegistration registration = cancellation.Register(() =>
            {
                Exception failure = null;
                try
                {
                    killTree();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }

                cancellationKill.SetResult(failure);
            });

            IOException readFailure = null;
            try
            {
                return await read();
            }
            catch (IOException ex)
            {
                readFailure = ex;
                throw;
            }
            finally
            {
                // An empty registration can mean either no callback or an inline callback that already finished.
                bool cancellationOwnsKill = registration.Token.CanBeCanceled
                    ? !registration.Unregister()
                    : cancellationKill.Task.IsCompleted;
                Exception failure = cancellationOwnsKill ? await cancellationKill.Task : null;

                // A successful kill can return before the process exits. Do not kill it again.
                if (!cancellationOwnsKill || failure != null)
                {
                    try
                    {
                        if (!hasExited())
                        {
                            killTree();
                        }
                    }
                    catch (Exception cleanupFailure) when (failure != null || readFailure != null)
                    {
                        failure = failure == null ? cleanupFailure : new AggregateException(failure, cleanupFailure);
                    }
                }

                if (failure != null)
                {
                    if (readFailure != null)
                    {
                        AggregateException combinedFailure = new AggregateException(readFailure, failure);
                        if (IsNativeProcessError(failure))
                        {
                            throw new IOException(readFailure.Message, combinedFailure);
                        }

                        throw combinedFailure;
                    }

                    ExceptionDispatchInfo.Throw(failure);
                }
            }
        }

        private static bool IsNativeProcessError(Exception error)
        {
            if (error is AggregateException aggregate)
            {
                var errors = aggregate.InnerExceptions;
                return errors.Count > 0 && errors.All(IsNativeProcessError);
            }

            // Process tree enumeration can wrap a failed native snapshot in InvalidOperationException.
            return error is Win32Exception or InvalidOperationException { InnerException: Win32Exception };
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
