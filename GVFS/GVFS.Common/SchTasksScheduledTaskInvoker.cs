using GVFS.Common.Tracing;
using System;
using System.IO;

namespace GVFS.Common
{
    /// <summary>
    /// Default <see cref="IScheduledTaskInvoker"/> implementation: shells out
    /// to <c>schtasks.exe</c>. Windows-only by nature -- on non-Windows the
    /// process launch fails and operations return false with a populated
    /// error message. User-mode install is Windows-only, so that's fine.
    /// </summary>
    public class SchTasksScheduledTaskInvoker : IScheduledTaskInvoker
    {
        private readonly ITracer tracer;

        public SchTasksScheduledTaskInvoker(ITracer tracer)
        {
            ArgumentNullException.ThrowIfNull(tracer);
            this.tracer = tracer;
        }

        public bool TryRegisterFromXml(string taskPath, string xml, out string errorMessage)
        {
            // schtasks /Create accepts an XML file path via /XML, not raw
            // XML on stdin. Write to a temp file with the same UTF-16 BOM
            // the Task Scheduler XML schema expects, then run schtasks.
            string tempPath = Path.Combine(Path.GetTempPath(), $"gvfs-task-{Guid.NewGuid():N}.xml");
            try
            {
                File.WriteAllText(tempPath, xml, new System.Text.UnicodeEncoding(bigEndian: false, byteOrderMark: true));

                // /F overwrites if already exists.
                ProcessResult result = ProcessHelper.Run(
                    "schtasks.exe",
                    $"/Create /TN \"{taskPath}\" /XML \"{tempPath}\" /F");

                if (result.ExitCode != 0)
                {
                    errorMessage = $"schtasks /Create failed (exit {result.ExitCode}): {result.Output.Trim()} {result.Errors.Trim()}".Trim();
                    return false;
                }

                errorMessage = string.Empty;
                return true;
            }
            catch (Exception e)
            {
                errorMessage = $"Failed to register scheduled task: {e}";
                this.tracer.RelatedError(errorMessage);
                return false;
            }
            finally
            {
                try { File.Delete(tempPath); } catch { /* best-effort cleanup */ }
            }
        }

        public bool TryQueryXml(string taskPath, out string xml, out string errorMessage)
        {
            xml = null;
            try
            {
                ProcessResult result = ProcessHelper.Run(
                    "schtasks.exe",
                    $"/Query /TN \"{taskPath}\" /XML");

                if (result.ExitCode != 0)
                {
                    // Exit 1 = task not found. Surface a useful message; the
                    // caller distinguishes "not found" from "permission denied"
                    // by inspecting the message text or just treating both as
                    // "not current".
                    errorMessage = $"schtasks /Query failed (exit {result.ExitCode}): {result.Output.Trim()} {result.Errors.Trim()}".Trim();
                    return false;
                }

                xml = result.Output;
                errorMessage = string.Empty;
                return true;
            }
            catch (Exception e)
            {
                errorMessage = $"Failed to query scheduled task: {e}";
                this.tracer.RelatedError(errorMessage);
                return false;
            }
        }

        public bool TryUnregister(string taskPath, out string errorMessage)
        {
            try
            {
                ProcessResult result = ProcessHelper.Run(
                    "schtasks.exe",
                    $"/Delete /TN \"{taskPath}\" /F");

                if (result.ExitCode != 0)
                {
                    // Exit 1 with "cannot find the file specified" means the
                    // task is already gone; treat as success.
                    string combined = (result.Output + " " + result.Errors).ToLowerInvariant();
                    if (combined.Contains("cannot find the file") || combined.Contains("system cannot find"))
                    {
                        errorMessage = string.Empty;
                        return true;
                    }

                    errorMessage = $"schtasks /Delete failed (exit {result.ExitCode}): {result.Output.Trim()} {result.Errors.Trim()}".Trim();
                    return false;
                }

                errorMessage = string.Empty;
                return true;
            }
            catch (Exception e)
            {
                errorMessage = $"Failed to unregister scheduled task: {e}";
                this.tracer.RelatedError(errorMessage);
                return false;
            }
        }
    }
}
