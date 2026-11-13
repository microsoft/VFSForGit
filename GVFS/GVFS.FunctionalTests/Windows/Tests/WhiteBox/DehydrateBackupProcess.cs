using GVFS.Common.Tracing;
using GVFS.FunctionalTests.Tools;
using GVFS.Platform.Windows;
using Microsoft.Win32.SafeHandles;
using NUnit.Framework;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace GVFS.FunctionalTests.Tests.WhiteBox
{
    /// <summary>
    /// Runs client-side test operations separately from the ProjFS provider process.
    /// </summary>
    internal static class DehydrateBackupProcess
    {
        public const string Switch = "--whitebox-dehydrate-backup";
        public const string NotificationControl = "notification-control.bin";
        public static readonly byte[] Content = Enumerable.Repeat((byte)'x', 4096).ToArray();
        private const int FileDispositionInfoEx = 21;
        private const uint DispositionDelete = 1;

        public static int Run(string[] args)
        {
            if (args.Length != 4 || (args[1] != "control" && args[1] != "moved-control" && args[1] != "delete"))
            {
                throw new ArgumentException("Specify the white-box operation, fixture root, and backup name.");
            }

            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(args[2]));
            string name = Path.GetFileName(root);
            string prefix = nameof(DehydrateBackupDeletionTests);
            if (!string.Equals(Path.GetDirectoryName(root), Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())), StringComparison.OrdinalIgnoreCase) ||
                !name.StartsWith(prefix, StringComparison.Ordinal) ||
                !Guid.TryParseExact(name.AsSpan(prefix.Length), "N", out _))
            {
                throw new ArgumentException("The white-box helper requires a disposable dehydrate-backup test fixture.");
            }

            if (args[1] == "control")
            {
                DeleteControlFile(Path.Combine(root, "src", NotificationControl));
                return 0;
            }

            if (args[1] == "moved-control")
            {
                using (WindowsDehydrateBackup scope = WindowsDehydrateBackup.Open(NullTracer.Instance, root, args[3]))
                {
                    DeleteControlFile(Path.Combine(root, "dehydrate_backup", args[3], "tree", "cached", NotificationControl));
                }

                return 0;
            }

            int liveReads = 0;
            using (WindowsDehydrateBackup scope = WindowsDehydrateBackup.Open(NullTracer.Instance, root, args[3]))
            {
                WindowsDehydrateBackup.DeletionResult result = scope.Delete(progress: _ =>
                {
                    byte[] data = File.ReadAllBytes(Path.Combine(root, "src", $"health-{liveReads}.bin"));
                    if (!data.SequenceEqual(Content))
                    {
                        throw new IOException("The live provider returned unexpected contents during backup deletion.");
                    }

                    ++liveReads;
                });
                Console.WriteLine(JsonSerializer.Serialize(new Summary
                {
                    FilesDeleted = result.FilesDeleted,
                    DirectoriesDeleted = result.DirectoriesDeleted,
                    LiveReads = liveReads,
                    Failures = result.FailureSamples.Select(failure => failure.Path + ": " + failure.Exception).ToArray(),
                }));
                return result.Succeeded ? 0 : 1;
            }
        }

        public static Summary Delete(string root, string backupName)
        {
            ProcessResult result = Start("delete", root, backupName);
            return JsonSerializer.Deserialize<Summary>(result.Output)
                ?? throw new IOException("The backup test process returned no deletion summary.");
        }

        public static void DeleteControl(string root)
        {
            Start("control", root, string.Empty);
        }

        public static void DeleteMovedControl(string root, string backupName)
        {
            Start("moved-control", root, backupName);
        }

        private static void DeleteControlFile(string path)
        {
            // Follow normal client opens, then verify the pinned target before arming deletion.
            using (SafeFileHandle handle = CreateFileW(
                @"\\?\" + path,
                (uint)(GVFS.Common.NativeMethods.FileAccess.DELETE | GVFS.Common.NativeMethods.FileAccess.FILE_READ_ATTRIBUTES),
                FileShare.Read, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero))
            {
                if (handle.IsInvalid)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to open the notification control.");
                }

                StringBuilder finalPath = new StringBuilder(32768);
                uint length = GetFinalPathNameByHandleW(handle, finalPath, (uint)finalPath.Capacity, 0);
                if (length == 0)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to resolve the notification control.");
                }

                if (length >= finalPath.Capacity ||
                    !string.Equals(finalPath.ToString(), @"\\?\" + path, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("The notification control resolves outside the disposable fixture path.");
                }

                uint disposition = DispositionDelete;
                if (!SetFileInformationByHandle(handle, FileDispositionInfoEx, ref disposition, sizeof(uint)))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to delete the notification control.");
                }
            }
        }

        private static ProcessResult Start(string operation, string root, string backupName)
        {
            ProcessStartInfo info = new ProcessStartInfo
            {
                FileName = Path.Combine(TestContext.CurrentContext.TestDirectory, "GVFS.FunctionalTests.exe"),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            info.ArgumentList.Add(Switch);
            info.ArgumentList.Add(operation);
            info.ArgumentList.Add(root);
            info.ArgumentList.Add(backupName);
            ProcessResult result = ProcessHelper.Run(info, timeoutMs: 30_000);
            Assert.That(result.ExitCode, Is.Zero, result.Errors + Environment.NewLine + result.Output);
            return result;
        }

        internal sealed class Summary
        {
            public long FilesDeleted { get; set; }
            public long DirectoriesDeleted { get; set; }
            public int LiveReads { get; set; }
            public string[] Failures { get; set; }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(
            string path, uint access, FileShare share, IntPtr security, FileMode mode, uint flags, IntPtr template);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint size, uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, ref uint flags, uint size);
    }
}
