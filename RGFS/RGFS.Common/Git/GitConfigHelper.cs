using System;
using System.Collections.Generic;

namespace RGFS.Common.Git
{
    /// <summary>
    /// Helper methods for git config-style file reading and parsing.
    /// </summary>
    public static class GitConfigHelper
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
            int commentIndex = sanitizedLine.IndexOf(RGFSConstants.GitCommentSign);
            if (commentIndex >= 0)
            {
                sanitizedLine = sanitizedLine.Substring(0, commentIndex);
            }

            sanitizedLine = sanitizedLine.Trim();

            return !string.IsNullOrWhiteSpace(sanitizedLine);
        }

        /// <summary>
        /// Get the settings for a section in a given config file.
        /// </summary>
        /// <param name="configLines">The contents of a config file, one line per entry.</param>
        /// <param name="sectionName">The name of the section to grab the settings from.</param>
        /// <returns>A dictionary of settings, keyed off the setting name.</returns>
        public static Dictionary<string, GitConfigSetting> GetSettings(string[] configLines, string sectionName)
        {
            List<string> linesToParse = new List<string>();

            int currentLineIndex = 0;
            string sectionTag = "[" + sectionName + "]";

            // There can be multiple occurrences of the same section in a config file.
            while (currentLineIndex < configLines.Length)
            {
                while (currentLineIndex < configLines.Length && !string.Equals(configLines[currentLineIndex].Trim(), sectionTag, StringComparison.OrdinalIgnoreCase))
                {
                    currentLineIndex++;
                }

                if (currentLineIndex < configLines.Length)
                {
                    // skip [sectionName] line
                    currentLineIndex++;

                    while (currentLineIndex < configLines.Length && !configLines[currentLineIndex].StartsWith("["))
                    {
                        string currentLineValue = configLines[currentLineIndex].Trim();
                        if (!string.IsNullOrEmpty(currentLineValue))
                        {
                            linesToParse.Add(currentLineValue);
                        }

                        currentLineIndex++;
                    }
                }
            }

            return ParseKeyValues(linesToParse);
        }

        /// <summary>
        /// Returns a list of settings based on a collection of lines of text in the form:
        ///     settingName = settingValue
        /// or
        ///     section.settingName=settingValue
        /// </summary>
        /// <param name="input">The lines of text with the settings to parse.</param>
        /// <returns>A dictionary of settings, keyed off the setting name representing the settings parsed from input.</returns>
        public static Dictionary<string, GitConfigSetting> ParseKeyValues(IEnumerable<string> input)
        {
            Dictionary<string, GitConfigSetting> configSettings = new Dictionary<string, GitConfigSetting>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in input)
            {
                string[] fields = line.Trim().Split(new[] { '=' }, 2, StringSplitOptions.None);

                if (fields.Length > 0)
                {
                    string key = fields[0].Trim();
                    string value = string.Empty;

                    if (fields.Length > 1)
                    {
                        value = fields[1].Trim();
                    }

                    if (!string.IsNullOrEmpty(key))
                    {
                        if (!configSettings.ContainsKey(key) && fields.Length == 2)
                        {
                            GitConfigSetting setting = new GitConfigSetting(key, value);
                            configSettings.Add(key, setting);
                        }
                        else if (fields.Length == 2)
                        {
                            configSettings[key].Add(value);
                        }
                    }
                }
            }

            return configSettings;
        }

        /// <summary>
        /// Returns a list of settings based on input of the form:
        ///     settingName1 = settingValue1
        ///     settingName2 = settingValue2
        ///     settingName3 = settingValue3
        ///     settingNameN = settingValueN
        /// or
        ///     section.settingName1=settingValue1
        ///     section.settingName2=settingValue2
        ///     section.settingName3=settingValue3
        ///     section.settingNameN=settingValueN
        /// </summary>
        /// <param name="input">The settings as text.</param>
        /// <returns>A dictionary of settings, keyed off the setting name representing the settings parsed from input.</returns>
        public static Dictionary<string, GitConfigSetting> ParseKeyValues(string input)
        {
            return ParseKeyValues(input.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
