using System;

namespace GVFS.Common
{
    public static class AntiVirusExclusions
    {
        public static bool AddAntiVirusExclusion(string path, out string error)
        {
            path = CleanPath(path);

            try
            {
                ProcessResult result = CallPowershellCommand("Add-MpPreference -ExclusionPath \"" + path + "\"");

                error = result.Errors;
                return result.ExitCode == 0 && string.IsNullOrEmpty(result.Errors);
            }
            catch (Exception e)
            {
                error = e.ToString();
                return false;
            }
        }

        public static bool TryGetIsPathExcluded(string path, out bool isExcluded, out string error)
        {
            isExcluded = false;
            path = CleanPath(path);
            try
            {
                string[] exclusions;
                if (TryGetKnownExclusions(out exclusions, out error))
                {
                    foreach (string excludedPath in exclusions)
                    {
                        if (excludedPath.Trim().Equals(path, StringComparison.OrdinalIgnoreCase))
                        {
                            isExcluded = true;
                            break;
                        }
                    }

                    return true;
                }

                return false;
            }
            catch (Exception e)
            {
                error = "Unable to get exclusions:" + e.ToString();
                return false;
            }
        }

        private static bool TryGetKnownExclusions(out string[] exclusions, out string error)
        {
            ProcessResult getMpPrefrencesResult = CallPowershellCommand("Get-MpPreference | Select -ExpandProperty ExclusionPath");

            // In some cases (like cmdlet not found), the exitCode == 0 but there will be errors and the output will be empty, handle this situation.
            if (getMpPrefrencesResult.ExitCode != 0 || 
                (string.IsNullOrEmpty(getMpPrefrencesResult.Output) && !string.IsNullOrEmpty(getMpPrefrencesResult.Errors)))
            {
                error = "Error while running PowerShell command to discover Defender exclusions. \n" + getMpPrefrencesResult.Errors;
                exclusions = null;
                return false;
            }

            exclusions = getMpPrefrencesResult.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            error = null;
            return true;
        }

        private static ProcessResult CallPowershellCommand(string command)
        {
            return ProcessHelper.Run("powershell.exe", "-NonInteractive -NoProfile -Command \"& { " + command + " }\"");
        }

        private static string CleanPath(string path)
        {
            // Remove trailing backslashes since exclusions will never have them and Add will fail with them
            return path.TrimEnd(GVFSConstants.PathSeparator);
        }
    }
}
