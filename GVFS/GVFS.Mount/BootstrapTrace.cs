using System;
using System.IO;

namespace GVFS.Mount
{
    /// <summary>
    /// Repro-only. Records mount process milestones in a plain file, independent of the
    /// tracer, to show how a mount process can exit before it writes any log output.
    /// </summary>
    internal static class BootstrapTrace
    {
        private const string DiagnosticsDirVariable = "GVFS_TEST_DIAGNOSTICS_DIR";
        private const string DefaultDiagnosticsDir = @"C:\temp\gvfs-ft-diagnostics";

        private static string path;

        public static void Initialize(string[] args)
        {
            try
            {
                string dir = Environment.GetEnvironmentVariable(DiagnosticsDirVariable);
                if (string.IsNullOrEmpty(dir))
                {
                    dir = DefaultDiagnosticsDir;
                }

                dir = Path.Combine(dir, "mount-bootstrap");
                Directory.CreateDirectory(dir);
                path = Path.Combine(dir, "mount_" + Environment.ProcessId + ".log");

                Write("start args=" + string.Join(" ", args));
                AppDomain.CurrentDomain.ProcessExit += (sender, e) => Write("ProcessExit ExitCode=" + Environment.ExitCode);
                AppDomain.CurrentDomain.UnhandledException += (sender, e) => Write("UnhandledException " + e.ExceptionObject);
            }
            catch
            {
                path = null;
            }
        }

        public static void Write(string message)
        {
            if (path == null)
            {
                return;
            }

            try
            {
                File.AppendAllText(path, DateTime.UtcNow.ToString("o") + " " + message + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}
