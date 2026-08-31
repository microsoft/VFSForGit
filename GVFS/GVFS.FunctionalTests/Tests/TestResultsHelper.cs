using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.FunctionalTests.Tests
{
    public static class TestResultsHelper
    {
        public static void OutputGVFSLogs(GVFSFunctionalTestEnlistment enlistment)
        {
            if (enlistment == null)
            {
                return;
            }

            Console.WriteLine("GVFS logs output attached below.\n\n");

            foreach (string filename in GetAllFilesInDirectory(enlistment.GVFSLogsRoot))
            {
                if (filename.Contains("mount_process"))
                {
                    // Validate that all mount processes started by the functional tests were started
                    // by verbs, and that "StartedByVerb" was set to true when the mount process was launched
                    OutputFileContents(
                        filename,
                        contents => contents.ShouldContain("\"StartedByVerb\":true"));
                }
                else
                {
                    OutputFileContents(filename);
                }
            }
        }

        public static void OutputFileContents(string filename, Action<string> contentsValidator = null)
        {
            try
            {
                using (StreamReader reader = new StreamReader(new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                {
                    Console.WriteLine("----- {0} -----", filename);

                    string contents = reader.ReadToEnd();

                    if (contentsValidator != null)
                    {
                        contentsValidator(contents);
                    }

                    Console.WriteLine(contents + "\n\n");
                }
            }
            catch (IOException ex)
            {
                Console.WriteLine("Unable to read logfile at {0}: {1}", filename, ex.ToString());
            }
        }

        public static IEnumerable<string> GetAllFilesInDirectory(string folderName)
        {
            DirectoryInfo directory = new DirectoryInfo(folderName);
            if (!directory.Exists)
            {
                return Enumerable.Empty<string>();
            }

            return directory.GetFiles().Select(file => file.FullName);
        }

        /// <summary>
        /// Root directory under which per-failure diagnostics (preserved logs and
        /// mount process dumps) are written so CI can upload them as an artifact.
        /// Honors the GVFS_TEST_DIAGNOSTICS_DIR environment variable; otherwise
        /// falls back to a folder under the temp path.
        /// </summary>
        public static string DiagnosticsRoot
        {
            get
            {
                string configured = Environment.GetEnvironmentVariable("GVFS_TEST_DIAGNOSTICS_DIR");
                return string.IsNullOrWhiteSpace(configured)
                    ? Path.Combine(Path.GetTempPath(), "gvfs_ft_diagnostics")
                    : configured;
            }
        }

        /// <summary>
        /// Copies every file in <paramref name="sourceFolder"/> into
        /// <paramref name="destinationFolder"/>. A mount that hung or exited
        /// abnormally may still hold its log file open, so a plain copy can fail
        /// with a sharing violation. In that case we fall back to opening the file
        /// with a read-only shared handle (FileShare.ReadWrite | Delete) and copy
        /// out whatever has been flushed so far — partial content is still useful.
        /// Best-effort: never throws.
        /// </summary>
        public static void CopyFilesWithFallback(string sourceFolder, string destinationFolder)
        {
            try
            {
                Directory.CreateDirectory(destinationFolder);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[DIAGNOSTICS] Unable to create '{destinationFolder}': {ex.Message}");
                return;
            }

            foreach (string sourceFile in GetAllFilesInDirectory(sourceFolder))
            {
                string destinationFile = Path.Combine(destinationFolder, Path.GetFileName(sourceFile));

                try
                {
                    File.Copy(sourceFile, destinationFile, overwrite: true);
                }
                catch (Exception copyException) when (copyException is IOException || copyException is UnauthorizedAccessException)
                {
                    // The file is likely locked by a still-running (possibly hung)
                    // mount process. Fall back to a shared read-only handle and copy
                    // what we can.
                    if (!TryCopyWithSharedReadHandle(sourceFile, destinationFile))
                    {
                        Console.Error.WriteLine($"[DIAGNOSTICS] Failed to copy '{sourceFile}' (locked): {copyException.Message}");
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[DIAGNOSTICS] Failed to copy '{sourceFile}': {ex.Message}");
                }
            }
        }

        private static bool TryCopyWithSharedReadHandle(string sourceFile, string destinationFile)
        {
            try
            {
                using (FileStream source = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (FileStream destination = new FileStream(destinationFile, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    source.CopyTo(destination);
                }

                Console.Error.WriteLine($"[DIAGNOSTICS] Copied '{sourceFile}' via shared read handle (may be partial)");
                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[DIAGNOSTICS] Shared-handle copy of '{sourceFile}' failed: {ex.Message}");
                return false;
            }
        }
    }
}
