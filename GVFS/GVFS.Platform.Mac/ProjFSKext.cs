using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using PrjFSLib.Mac;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace GVFS.Platform.Mac
{
    public class ProjFSKext : IKernelDriver
    {
        private const string DriverName = "io.gvfs.PrjFSKext";

        public bool EnumerationExpandsDirectories { get; } = true;

        public string LogsFolderPath
        {
            get
            {
                return Path.Combine(System.IO.Path.GetTempPath(), "PrjFSKext");
            }
        }

        public bool IsGVFSUpgradeSupported()
        {
            return false;
        }

        public bool IsSupported(string normalizedEnlistmentRootPath, out string warning, out string error)
        {
            warning = null;
            error = null;

            string pathRoot = Path.GetPathRoot(normalizedEnlistmentRootPath);
            DriveInfo rootDriveInfo = DriveInfo.GetDrives().FirstOrDefault(x => x.Name == pathRoot);
            if (rootDriveInfo == null)
            {
                warning = $"Unable to ensure that '{normalizedEnlistmentRootPath}' is an APFS or HFS+ volume.";
            }
            else if (!string.Equals(rootDriveInfo.DriveFormat, "APFS", StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(rootDriveInfo.DriveFormat, "HFS", StringComparison.OrdinalIgnoreCase))
            {
                error = $"Error: Currently only APFS and HFS+ volumes are supported.  Ensure repo is located into an APFS or HFS+ volume.";
                return false;
            }

            return true;
        }

        public bool TryFlushLogs(out string error)
        {
            Directory.CreateDirectory(this.LogsFolderPath);
            ProcessResult logShowOutput = ProcessHelper.Run("log", args: "show --predicate \"subsystem contains \'org.vfsforgit\'\" --info", redirectOutput: true);
            File.WriteAllText(Path.Combine(this.LogsFolderPath, "PrjFSKext.log"), logShowOutput.Output);
            error = string.Empty;

            return true;
        }

        public bool IsReady(JsonTracer tracer, string enlistmentRoot, out string error)
        {
            error = null;
            return
                this.IsKextLoaded() ||
                this.TryLoadKext(tracer, enlistmentRoot, out error);
        }

        public bool TryPrepareFolderForCallbacks(string folderPath, out string error, out Exception exception)
        {
            exception = null;
            error = string.Empty;
            Result result = VirtualizationInstance.ConvertDirectoryToVirtualizationRoot(folderPath);
            if (result != Result.Success)
            {
                error = "Failed to prepare \"" + folderPath + "\" for callbacks, error: " + result.ToString("F");
                return false;
            }

            return true;
        }

        private ProcessResult Bash(string cmd)
        {
            string escapedArgs = cmd.Replace("\"", "\\\"");

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"{escapedArgs}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using (Process process = new Process())
            {
                process.StartInfo = startInfo;
                string errors = string.Empty;
                process.ErrorDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                    {
                        errors += args.Data + "\r\n";
                    }
                };

                process.Start();
                process.BeginErrorReadLine();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return new ProcessResult(output, errors, process.ExitCode);
            }
        }

        private bool TryLoadKext(ITracer tracer, string enlistmentRoot, out string error)
        {
            Console.WriteLine("Kernel extension not loaded.  Attempting to load...");
            error = DriverName + " is not loaded. Make sure the driver is loaded and try again.";
            ProcessResult loadKext = this.Bash("sudo kextutil /Library/Extensions/PrjFSKext.kext");
            if (loadKext.ExitCode == 0)
            {
                error = null;
                return true;
            }
            else if (loadKext.ExitCode == 27)
            {
                tracer.RelatedWarning("Kext unable to load. Possibly has not been approved by the user");
                error = DriverName + @" was unable to load.  Please check and make sure you have allowed the extension in
System Preferences -> Security & Privacy";
            }
            else
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("ExitCode", loadKext.ExitCode);
                metadata.Add("Output", loadKext.Output);
                metadata.Add("Errors", loadKext.Errors);
                tracer.RelatedWarning(metadata, "Failed to load kext");
            }

            return false;
        }

        private bool IsKextLoaded()
        {
            ProcessResult loadedKexts = ProcessHelper.Run("kextstat", args: "-b " + DriverName, redirectOutput: true);
            return loadedKexts.Output.Contains(DriverName);
        }
    }
}
