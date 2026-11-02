using System;
using System.Threading;

namespace GVFS.Common.NamedPipes
{
    /// <summary>
    /// The process-level effects <see cref="MountFailureReporter"/> depends on: waiting,
    /// sleeping, and ending the process. Injected so unit tests can observe what was
    /// requested without real delays and without ending the test host.
    /// </summary>
    internal interface IMountFailureEnvironment
    {
        /// <returns>True if <paramref name="signal"/> was set before the timeout.</returns>
        bool WaitForSignal(WaitHandle signal, int timeoutMs);

        void Sleep(int durationMs);

        void ExitProcess(int exitCode);

        /// <summary>
        /// Parks the calling thread until another thread ends the process.
        /// </summary>
        void BlockUntilProcessExits();
    }

    internal sealed class ProcessMountFailureEnvironment : IMountFailureEnvironment
    {
        public bool WaitForSignal(WaitHandle signal, int timeoutMs)
        {
            return signal.WaitOne(timeoutMs);
        }

        public void Sleep(int durationMs)
        {
            Thread.Sleep(durationMs);
        }

        public void ExitProcess(int exitCode)
        {
            Environment.Exit(exitCode);
        }

        public void BlockUntilProcessExits()
        {
            Thread.Sleep(Timeout.Infinite);
        }
    }
}
