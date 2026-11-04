using GVFS.Common;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace GVFS.Hooks
{
    internal static class GitCommandResolver
    {
        public const uint InvalidFileAttributes = 0xFFFFFFFF;
        public const string UnableToAccess = "unable to access '";

        private const uint FileAttributeDirectory = 0x10;

        // Git builds each PATH candidate in a char[MAX_PATH] buffer.
        private const int GitMaxPath = 260;

        /// <summary>
        /// Determines whether Git will expand <paramref name="hookCommand"/> as an alias. Returns
        /// false when GVFS cannot tell: PATH is empty, or the alias config keys cannot be read or
        /// are incomplete. The caller must not guess, because an outer lock on a genuine alias
        /// blocks the commands of that alias.
        /// </summary>
        public static bool TryIsAlias(
            string hookCommand,
            Func<ProcessResult> readAliasKeys,
            string searchPath,
            Func<string, uint> getFileAttributes,
            out bool isAlias,
            out string error)
        {
            isAlias = false;
            error = null;

            // Git supplies canonical, unprefixed names for built-ins, and git-<name> for external attempts.
            if (!hookCommand.StartsWith("git-", StringComparison.Ordinal) ||
                hookCommand.IndexOfAny(new[] { '\\', '/' }) >= 0)
            {
                return true;
            }

            string command = hookCommand.Substring("git-".Length);
            if (string.IsNullOrEmpty(searchPath))
            {
                // Git adds its exec-path to PATH before it runs the hook, so this is a defensive check.
                error = $"Unable to resolve Git command '{command}': the inherited PATH is empty.";
                return false;
            }

            if (!TryFindProgramOnPath(hookCommand, searchPath, getFileAttributes, out bool programFound, out error))
            {
                return false;
            }

            if (programFound)
            {
                return true;
            }

            ProcessResult result;
            try
            {
                result = readAliasKeys();
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException ||
                ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException)
            {
                error = $"Unable to read Git aliases: {FormatError(ex)}";
                return false;
            }

            // git config exits with 1 when no key matches. It exits with 128 when it cannot read a
            // config file, after warnings that name the file.
            bool noAliases = result.ExitCode == 1 && string.IsNullOrEmpty(result.Output) &&
                (result.Errors ?? string.Empty).IndexOf(UnableToAccess, StringComparison.Ordinal) < 0;
            if (result.ExitCode != 0 && !noAliases)
            {
                error = $"Unable to read Git aliases (exit code {result.ExitCode}). {result.Errors}".TrimEnd();
                return false;
            }

            foreach (string key in (result.Output ?? string.Empty).Split('\n'))
            {
                if (IsAliasKeyFor(key, command))
                {
                    isAlias = true;
                    return true;
                }
            }

            return true;
        }

        public static string GetProcessPath(int processId)
        {
            try
            {
                using (Process process = Process.GetProcessById(processId))
                {
                    string path = process.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(path))
                    {
                        return path;
                    }
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is Win32Exception || ex is InvalidOperationException)
            {
                throw new InvalidOperationException($"Unable to find the executable of the parent Git process (PID {processId}): {ex.Message}", ex);
            }

            throw new InvalidOperationException($"Unable to find the executable of the parent Git process (PID {processId}).");
        }

        public static ProcessStartInfo CreateAliasKeyQuery(string gitPath)
        {
            return CreateConfigQuery(gitPath, "--name-only", "--get-regexp", @"^alias\.");
        }

        public static ProcessStartInfo CreateConfigBoolQuery(string gitPath, string key)
        {
            return CreateConfigQuery(gitPath, "--type=bool", key);
        }

        public static uint GetFileAttributes(string path)
        {
            return NativeMethods.GetFileAttributesW(path);
        }

        private static ProcessStartInfo CreateConfigQuery(string gitPath, params string[] arguments)
        {
            ProcessStartInfo processInfo = new ProcessStartInfo(gitPath);
            processInfo.UseShellExecute = false;

            // The query reads no input. Do not give it the hook's standard input.
            processInfo.RedirectStandardInput = true;
            processInfo.RedirectStandardOutput = true;
            processInfo.RedirectStandardError = true;

            // Git writes config names in UTF-8, whatever the console code page is.
            processInfo.StandardOutputEncoding = Encoding.UTF8;
            processInfo.StandardErrorEncoding = Encoding.UTF8;
            processInfo.ArgumentList.Add("config");
            foreach (string argument in arguments)
            {
                processInfo.ArgumentList.Add(argument);
            }

            processInfo.Environment["COMMAND_HOOK_LOCK"] = "true";
            return processInfo;
        }

        /// <summary>
        /// Mirrors config_alias_cb in Git's alias.c. Like parse_config_key, it splits the text
        /// after "alias." at the last dot into a subsection and a variable name. If the
        /// subsection is empty, the variable name is the alias name, matched for any ASCII case.
        /// If the variable name is "command", the alias name is the subsection, which must match
        /// exactly. Otherwise the alias name is all of the text after "alias.", matched for any
        /// ASCII case. Thus "alias..review.foo" names ".review.foo".
        /// </summary>
        private static bool IsAliasKeyFor(string key, string command)
        {
            const string Section = "alias.";
            if (!key.StartsWith(Section, StringComparison.Ordinal))
            {
                return false;
            }

            string name = key.Substring(Section.Length);
            int lastDot = name.LastIndexOf('.');
            if (lastDot >= 0)
            {
                string subsection = name.Substring(0, lastDot);
                string variable = name.Substring(lastDot + 1);
                if (subsection.Length == 0)
                {
                    name = variable;
                }
                else if (string.Equals(variable, "command", StringComparison.Ordinal))
                {
                    return string.Equals(subsection, command, StringComparison.Ordinal);
                }
            }

            return name.Length > 0 && EqualsIgnoringAsciiCase(name, command);
        }

        private static bool EqualsIgnoringAsciiCase(string left, string right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (int i = 0; i < left.Length; ++i)
            {
                if (ToAsciiLower(left[i]) != ToAsciiLower(right[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static char ToAsciiLower(char c)
        {
            return c >= 'A' && c <= 'Z' ? (char)(c + ('a' - 'A')) : c;
        }

        /// <summary>
        /// Mirrors path_lookup and lookup_prog in Git for Windows (compat/mingw.c). Git checks
        /// "&lt;entry&gt;\&lt;command&gt;.exe" for each nonempty PATH entry, then the same path without
        /// ".exe". It uses _waccess, which fails for any file that it cannot access. Git builds
        /// the path in a char[MAX_PATH] buffer and cuts a longer path. Returns false for a path
        /// that Git cuts, because the conversion of a cut path is not reproduced here.
        /// </summary>
        private static bool TryFindProgramOnPath(
            string hookCommand,
            string searchPath,
            Func<string, uint> getFileAttributes,
            out bool found,
            out string error)
        {
            found = false;
            error = null;
            bool isExe = hookCommand.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
            foreach (string directory in searchPath.Split(';'))
            {
                if (directory.Length == 0)
                {
                    continue;
                }

                string path = directory + "\\" + hookCommand + ".exe";
                if (Encoding.UTF8.GetByteCount(path) >= GitMaxPath)
                {
                    error = $"Unable to resolve Git command '{hookCommand.Substring("git-".Length)}': the PATH entry '{directory}' is too long for Git's path buffer.";
                    return false;
                }

                if (!isExe && getFileAttributes(path) != InvalidFileAttributes)
                {
                    found = true;
                    return true;
                }

                uint attributes = getFileAttributes(path.Substring(0, path.Length - ".exe".Length));
                if (attributes != InvalidFileAttributes && (attributes & FileAttributeDirectory) == 0)
                {
                    found = true;
                    return true;
                }
            }

            return true;
        }

        private static string FormatError(Exception error)
        {
            return $"{error.GetType().Name} (0x{error.HResult:X8}): {error.Message}";
        }

        private static class NativeMethods
        {
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern uint GetFileAttributesW(string fileName);
        }
    }
}
