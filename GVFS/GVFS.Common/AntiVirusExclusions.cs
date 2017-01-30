using System;

namespace GVFS.Common
{
    public static class AntiVirusExclusions
    {
        public static void AddAntiVirusExclusion(string path)
        {
            try
            {
                CallPowershellCommand("Add-MpPreference -ExclusionPath \"" + path + "\"");
            }
            catch (Exception e)
            {
                Console.WriteLine("Unable to add exclusion: " + e.ToString());
            }
        }

        public static bool TryGetIsPathExcluded(string path, out bool isExcluded)
        {
            isExcluded = false;

            try
            {
                ProcessResult getMpPrefrencesResult = CallPowershellCommand("Get-MpPreference | Select -ExpandProperty ExclusionPath");
                if (getMpPrefrencesResult.ExitCode == 0)
                {
                    foreach (string excludedPath in getMpPrefrencesResult.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (excludedPath.Trim().Equals(path, StringComparison.OrdinalIgnoreCase))
                        {
                            isExcluded = true;
                            break;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Unable to get exclusions:" + e.ToString());

                return false;
            }

            return true;
        }

        private static ProcessResult CallPowershellCommand(string command)
        {
            return ProcessHelper.Run("powershell", "-NoProfile -Command \"& { " + command + " }\"");
        }
    }
}
