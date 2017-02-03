namespace GVFS.GVFlt.DotGit
{
    public class GitConfigFileUtils
    {
        /// <summary>
        /// Sanitizes lines read from Git config files:
        /// - Removes leading and trailing whitespace
        /// - Removes comments
        /// </summary>
        /// <param name="fileLine">Input line from config file</param>
        /// <param name="sanitizedLine">Sanitized config file line</param>
        /// <returns>true if sanitizedLine has content, false if there is no content left after sanitizing</returns>
        public static bool TrySanitizeConfigFileLine(string fileLine, out string sanitizedLine)
        {
            sanitizedLine = fileLine;
            int commentIndex = sanitizedLine.IndexOf('#');
            if (commentIndex >= 0)
            {
                sanitizedLine = sanitizedLine.Substring(0, commentIndex);
            }

            sanitizedLine = sanitizedLine.Trim();

            return !string.IsNullOrWhiteSpace(sanitizedLine);
        }
    }
}
