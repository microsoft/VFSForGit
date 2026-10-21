using GVFS.Common;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace GVFS.Hooks
{
    internal static class GitCommandResolver
    {
        private const int InvalidNameHResult = unchecked((int)0x8007007B);

        public static bool ShouldLock(
            string hookCommand,
            Func<string, ProcessResult> getAlias,
            string searchPath,
            Func<string, bool> fileExists,
            Action<string> reportError)
        {
            if (!TryIsAlias(hookCommand, getAlias, searchPath, fileExists, out bool isAlias, out string error))
            {
                reportError(error + " GVFS will keep the command lock.");
                return true;
            }

            return !isAlias;
        }

        public static bool TryIsAlias(
            string hookCommand,
            Func<string, ProcessResult> getAlias,
            string searchPath,
            Func<string, bool> fileExists,
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
            ProcessResult result;
            try
            {
                result = getAlias("alias." + command);
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException ||
                ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException)
            {
                error = $"Unable to query Git alias '{command}': {FormatError(ex)}";
                return false;
            }

            if (result.ExitCode == 1 && string.IsNullOrEmpty(result.Output))
            {
                return true;
            }

            if (result.ExitCode != 0)
            {
                error = $"Unable to read Git alias '{command}' (exit code {result.ExitCode}). {result.Errors}".TrimEnd();
                return false;
            }

            if (string.IsNullOrWhiteSpace(result.Output))
            {
                return true;
            }

            if (string.IsNullOrEmpty(searchPath))
            {
                error = $"Unable to resolve Git command '{command}': the inherited PATH is empty.";
                return false;
            }

            string candidate = null;
            try
            {
                // Git adds its exec-path to PATH before the hook. Probe Windows names directly:
                // --list-cmds misses executable names with an uppercase GIT- prefix.
                foreach (string directory in searchPath.Split(Path.PathSeparator))
                {
                    if (directory.Length == 0)
                    {
                        continue;
                    }

                    string commandPath = directory.TrimEnd('\\', '/') + "\\" + hookCommand;
                    if (!hookCommand.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        candidate = commandPath + ".exe";
                        if (fileExists(candidate))
                        {
                            return true;
                        }
                    }

                    candidate = commandPath;
                    if (fileExists(candidate))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                error = $"Unable to resolve Git command '{command}' at '{candidate}': {FormatError(ex)}";
                return false;
            }

            isAlias = true;
            return true;
        }

        public static string GetProcessPath(int processId)
        {
            using (Process process = Process.GetProcessById(processId))
            {
                string path = process.MainModule?.FileName;
                if (string.IsNullOrEmpty(path))
                {
                    throw new InvalidOperationException($"Unable to read the executable path for Git PID {processId}.");
                }

                return path;
            }
        }

        public static ProcessStartInfo CreateAliasQuery(string gitPath, string aliasKey)
        {
            ProcessStartInfo processInfo = new ProcessStartInfo(gitPath);
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardInput = true;
            processInfo.RedirectStandardOutput = true;
            processInfo.RedirectStandardError = true;
            processInfo.ArgumentList.Add("config");
            processInfo.ArgumentList.Add("--get");
            processInfo.ArgumentList.Add(aliasKey);
            processInfo.Environment["COMMAND_HOOK_LOCK"] = "true";
            return processInfo;
        }

        public static bool FileExists(string path)
        {
            try
            {
                return (File.GetAttributes(path) & FileAttributes.Directory) == 0;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                return false;
            }
            catch (IOException ex) when (ex.HResult == InvalidNameHResult)
            {
                // Git also ignores invalid PATH entries, such as directories enclosed in literal quotes.
                return false;
            }
        }

        private static string FormatError(Exception error)
        {
            return $"{error.GetType().Name} (0x{error.HResult:X8}): {error.Message}";
        }
    }
}
