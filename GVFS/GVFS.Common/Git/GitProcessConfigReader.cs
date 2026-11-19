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
    public enum GitConfigBoolScope
    {
        Merged,
        System,
    }

    internal static class GitProcessConfigReader
    {
        private const int MaxOutput = 65536;
        private const int MaxErrors = 4096;

        internal static bool TryReadBool(
            string key,
            GitConfigBoolScope scope,
            Func<string, CancellationToken, GitProcess.Result> readConfig,
            TimeSpan timeout,
            ITracer tracer,
            out bool? value,
            out string error)
        {
            value = null;
            error = null;
            tracer = tracer ?? NullTracer.Instance;
            if (scope != GitConfigBoolScope.System && scope != GitConfigBoolScope.Merged)
            {
                throw new ArgumentOutOfRangeException(nameof(scope));
            }

            string arguments = scope == GitConfigBoolScope.System
                ? "config --system --includes --type=bool " + key
                : LibGit2Repo.GetConfigBoolArguments(key);

            using (CancellationTokenSource deadline = new CancellationTokenSource(timeout))
            {
                try
                {
                    GitProcess.Result result = readConfig(arguments, deadline.Token);
                    if (result.OutputTruncated || result.ErrorsTruncated)
                    {
                        error = $"Error while reading '{key}' from config: output was truncated.";
                        return false;
                    }

                    if (result.ExitCode == 1 && !string.IsNullOrWhiteSpace(result.Output))
                    {
                        error = $"Error while reading '{key}' from config: unexpected output for an unset value.";
                        return false;
                    }

                    return LibGit2Repo.TryParseConfigBoolOutput(key, result.ExitCode, result.Output, result.Errors, out value, out error);
                }
                catch (OperationCanceledException) when (deadline.IsCancellationRequested)
                {
                    error = $"The Git configuration read for '{key}' timed out.";
                    tracer.RelatedWarning(error);
                    return false;
                }
                catch (Exception e) when (e is GitConfigProcessException || e is Win32Exception || e is IOException || e is UnauthorizedAccessException)
                {
                    error = $"The Git configuration read for '{key}' failed: {e.Message}";
                    tracer.RelatedWarning($"{error} {e}");
                    return false;
                }
            }
        }

        // If the caller runs Git and returns a result, use Read for retries.
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

        // If the reader must run Git and manage its streams, use ReadRaw.
        // ReadRaw uses the same retry policy as Read.
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
                cancellation,
                () => process.WaitForExit(5000));

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
            CancellationToken cancellation,
            Func<bool> waitForExit = null)
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

                if (waitForExit != null)
                {
                    try
                    {
                        if (!hasExited() && !waitForExit())
                        {
                            throw new Win32Exception("The Git configuration process did not exit within the cleanup deadline.");
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
