using GVFS.Common;
using System;
using System.IO;

namespace GVFS.Platform.Windows
{
    public static class ProjFsAttachRemedy
    {
        public static string BuildForVolume(string volumeRoot)
        {
            string volume = string.IsNullOrEmpty(volumeRoot) ? "<volume>" : volumeRoot;
            if (volume.Length == 3 && volume[1] == ':' && volume[2] == '\\')
            {
                volume = volume.Substring(0, 2);
            }

            string argument = CommandLineEscaping.EscapeArgument(volume);
            return $"If {volume} is a Dev Drive, inspect its saved filter allowances from an elevated command prompt:"
                + Environment.NewLine
                + $"    fsutil devdrv query {argument}"
                + Environment.NewLine
                + "If PrjFlt is already allowed, close applications that use the volume and remount it, or restart the computer."
                + Environment.NewLine
                + "Otherwise, retain every entry under 'Filters allowed on this developer volume' and add PrjFlt:"
                + Environment.NewLine
                + $"    fsutil devdrv setFiltersAllowed /volume {argument} \"<existing volume filters>,PrjFlt\""
                + Environment.NewLine
                + "Replace the placeholder with those volume entries. If that section is absent, use \"PrjFlt\" as the list."
                + Environment.NewLine
                + "setFiltersAllowed replaces the volume list, so keep its existing entries. Do not omit /volume or add /f."
                + Environment.NewLine
                + "After the update, close applications that use the volume and remount it, or restart the computer. Retry gvfs mount.";
        }

        internal static string BuildForEnlistment(string enlistmentRoot)
        {
            string volume;
            try
            {
                volume = Path.GetPathRoot(enlistmentRoot);
            }
            catch (ArgumentException)
            {
                volume = null;
            }

            return BuildForVolume(volume);
        }
    }
}
