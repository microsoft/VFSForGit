using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace GVFS.Common.Git
{
    internal static class AuthenticationConfiguration
    {
        internal const string ReadCommand =
            "config --includes --null --show-scope --get-regexp \"^credential\\.(.*\\.)?(helper|usehttppath|interactive|azreposcredentialtype|msauthusebroker)$\"";
        private const int MaxOutput = 65536;
        private const int MaxEntries = 128;
        private const int TimeoutMilliseconds = 2000;

        // These are preferences, not evidence that GCM ran or used a broker.
        private static readonly Dictionary<string, string> EnvironmentSettings = new Dictionary<string, string>
        {
            { "azreposcredentialtype", "GCM_AZREPOS_CREDENTIALTYPE" },
            { "msauthusebroker", "GCM_MSAUTH_USEBROKER" },
            { "interactive", "GCM_INTERACTIVE" },
        };

        internal static EventMetadata Collect(
            string repositoryUrl,
            Func<string, Process> createProcess,
            Dictionary<string, string> credentialFillEnvironment = null,
            ITracer tracer = null)
        {
            EventMetadata metadata = new EventMetadata
            {
                { "SnapshotTiming", "after-failed-credential-fill" },
                { "EnvironmentTiming", credentialFillEnvironment == null ? "diagnostic-process" : "credential-fill-process" },
            };
            using (CancellationTokenSource deadline = new CancellationTokenSource(TimeoutMilliseconds))
            {
                try
                {
                    Dictionary<string, string> environment = null;
                    GitProcess.Result result = GitConfigReader.ReadRaw(
                        "credential",
                        () =>
                        {
                            Process read = createProcess(ReadCommand);
                            if (environment == null)
                            {
                                if (credentialFillEnvironment != null)
                                {
                                    CopyEnvironment(credentialFillEnvironment, read.StartInfo);
                                }

                                // GIT_CONFIG redirects config commands, but not credential fill.
                                read.StartInfo.Environment.Remove("GIT_CONFIG");
                                environment = read.StartInfo.Environment.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
                            }
                            else
                            {
                                CopyEnvironment(environment, read.StartInfo);
                            }

                            return read;
                        },
                        string.Empty,
                        deadline.Token,
                        tracer);
                    metadata["GitTerminalPrompt"] = environment.TryGetValue("GIT_TERMINAL_PROMPT", out string terminalPrompt) ?
                        DescribeValue("usehttppath", terminalPrompt) : "unset";
                    metadata["GcmValidate"] = environment.TryGetValue("GCM_VALIDATE", out string validate) ?
                        DescribeValue("msauthusebroker", validate) : "unset";
                    metadata["ConfigQueryExitCode"] = result.ExitCode;
                    if (result.OutputTruncated || result.ErrorsTruncated)
                    {
                        metadata["Status"] = "query-output-limit";
                        return metadata;
                    }

                    // Exit 1 means no matching keys only if there is no diagnostic output.
                    if ((result.ExitCode != 0 && result.ExitCode != 1) || result.Errors.Length != 0 ||
                        (result.ExitCode == 1 && result.Output.Length != 0))
                    {
                        metadata["Status"] = "config-query-error";
                        return metadata;
                    }

                    if (!TryParseEntries(result.Output, out List<Entry> entries))
                    {
                        metadata["Status"] = "config-format-or-entry-limit";
                        return metadata;
                    }

                    if (entries.Any(e => e.Url != null && e.Url.Any(char.IsControl)))
                    {
                        metadata["Status"] = "unsupported-config-url";
                        return metadata;
                    }

                    if (environment.TryGetValue("GIT_CONFIG_NOSYSTEM", out string noSystem) &&
                        DescribeValue("usehttppath", noSystem) == "true")
                    {
                        metadata["SystemConfigState"] = "disabled";
                    }
                    else if (entries.Any(e => e.Scope == "system"))
                    {
                        metadata["SystemConfigState"] = "auth-settings-present";
                    }
                    else
                    {
                        GitProcess.Result systemResult = GitConfigReader.ReadRaw(
                            "--system --list",
                            () =>
                            {
                                Process system = createProcess("config --system --null --name-only --list");
                                CopyEnvironment(environment, system.StartInfo);
                                return system;
                            },
                            string.Empty,
                            deadline.Token,
                            tracer);
                        metadata["SystemConfigState"] = systemResult.ExitCodeIsSuccess && systemResult.Errors.Length == 0 ?
                            "readable-no-auth-settings" :
                            systemResult.Errors.Contains("No such file or directory", StringComparison.Ordinal) ?
                            "missing" : "query-error";
                    }

                    if (!CredentialUrl.TryParse(repositoryUrl, out CredentialUrl url) || url.Scheme == null || url.Host == null)
                    {
                        metadata["Status"] = "unsupported-repository-url";
                        return metadata;
                    }

                    if (url.Host.Any(c => !char.IsAsciiLetterOrDigit(c) && !".-:[]".Contains(c)) ||
                        (url.Path != null && url.Path.IndexOfAny(new[] { '?', '#' }) >= 0))
                    {
                        metadata["Status"] = "unsupported-sensitive-repository-url";
                        return metadata;
                    }

                    // credential.c uses select_all, unlike config --get-urlmatch. Unique keys
                    // let Git match every entry without collapsing helper values by specificity.
                    StringBuilder matchConfig = new StringBuilder();
                    List<(Entry Entry, string Url)> fallbackCandidates = new List<(Entry, string)>();
                    for (int i = 0; i < entries.Count; ++i)
                    {
                        Entry entry = entries[i];
                        if (entry.Url == null)
                        {
                            entry.Matches = true;
                        }
                        else
                        {
                            if (!CredentialUrl.TryParse(entry.Url, out CredentialUrl partial))
                            {
                                metadata["Status"] = "unsupported-config-url";
                                return metadata;
                            }

                            string matchUrl = RedactUserInfo(entry.Url, partial.Username, url.Username);
                            if (matchUrl == null)
                            {
                                metadata["Status"] = "unsupported-config-url";
                                return metadata;
                            }

                            matchConfig.Append("[authdiagnostic \"").Append(EscapeConfig(matchUrl))
                                .Append("\"]\nentry").Append(i).Append(" = true\n");
                            if (partial.Matches(url))
                            {
                                // Missing scheme/host always fails native normalization.
                                entry.Matches = partial.Scheme == null || partial.Host == null;
                                if (!entry.Matches)
                                {
                                    fallbackCandidates.Add((entry, matchUrl));
                                }
                            }
                        }
                    }

                    if (matchConfig.Length > MaxOutput)
                    {
                        metadata["Status"] = "matching-input-limit";
                        return metadata;
                    }

                    if (matchConfig.Length > 0)
                    {
                        GitProcess.Result matched = GitConfigReader.ReadRaw(
                            "--get-urlmatch",
                            () => CreateMatcher(createProcess, environment, url.Format(redactUsername: true)),
                            matchConfig.ToString(),
                            deadline.Token,
                            tracer);
                        if ((matched.ExitCode != 0 && matched.ExitCode != 1) || matched.Errors.Length != 0 ||
                            matched.OutputTruncated || matched.ErrorsTruncated ||
                            !TryApplyMatches(matched.Output, entries))
                        {
                            metadata["Status"] = "url-match-query-error";
                            return metadata;
                        }
                    }

                    foreach (var candidate in fallbackCandidates.Where(c => !c.Entry.Matches))
                    {
                        // Do not decode a valid nonmatch into a match. If a full URL fails
                        // Git normalization, report the unsupported scope rather than guess.
                        GitProcess.Result normalized = GitConfigReader.ReadRaw(
                            "--get-urlmatch",
                            () => CreateMatcher(createProcess, environment, candidate.Url),
                            "[authdiagnostic]\nnormalized = true\n",
                            deadline.Token,
                            tracer);
                        if (!normalized.ExitCodeIsSuccess || normalized.Errors.Length != 0 ||
                            normalized.OutputTruncated || normalized.ErrorsTruncated)
                        {
                            metadata["Status"] = "unsupported-config-url";
                            return metadata;
                        }
                    }

                    AddSettings(metadata, entries, url, environment);
                    metadata["Status"] = "collected";
                    return metadata;
                }
                catch (OperationCanceledException)
                {
                    metadata["Status"] = "query-timeout";
                }
                catch (Win32Exception)
                {
                    metadata["Status"] = "query-process-error";
                }
                catch (IOException)
                {
                    metadata["Status"] = "query-io-error";
                }
            }

            return metadata;
        }

        internal static string DescribeHelper(string value)
        {
            if (value == null)
            {
                return "omitted:valueless";
            }

            if (value.Length == 0)
            {
                return "reset";
            }

            if (value[0] == '!')
            {
                return "omitted:shell";
            }

            string[] names = { "manager", "manager-core", "wincred", "cache", "store", "osxkeychain", "libsecret", "oauth" };
            if (names.Contains(value, StringComparer.Ordinal))
            {
                return value;
            }

            foreach (string name in names)
            {
                if (value.Length > name.Length && value.StartsWith(name, StringComparison.Ordinal) && char.IsWhiteSpace(value[name.Length]))
                {
                    return name + ":omitted-arguments";
                }
            }

            // Emit no caller-controlled path component, even when it looks like an install path.
            string path = value;
            bool quoted = false;
            if (path.Length > 1 && (path[0] == '"' || path[0] == '\'') && path[path.Length - 1] == path[0])
            {
                quoted = true;
                path = path.Substring(1, path.Length - 2);
            }

            string normalized = path.Replace('\\', '/');
            bool absolute = normalized.StartsWith("/", StringComparison.Ordinal) ||
                (normalized.Length > 2 && char.IsAsciiLetter(normalized[0]) && normalized[1] == ':' && normalized[2] == '/');
            if (absolute && (quoted || !normalized.Any(char.IsWhiteSpace)) &&
                !normalized.Any(c => char.IsControl(c) || "\"'`$;&|<>()".Contains(c)))
            {
                foreach (string name in names)
                {
                    string executable = "git-credential-" + name + ".exe";
                    if (normalized.EndsWith("/" + executable, StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (string layout in new[] { "mingw64/bin", "ucrt64/bin", "mingw32/bin" })
                        {
                            if (normalized.EndsWith("/" + layout + "/" + executable, StringComparison.OrdinalIgnoreCase))
                            {
                                return "<git-install>/" + layout + "/" + executable;
                            }
                        }

                        return "<redacted-path>/" + executable;
                    }
                }
            }

            return "omitted:unknown-or-arguments";
        }

        internal static bool TryParseEntries(string output, out List<Entry> entries)
        {
            entries = new List<Entry>();
            string[] fields = output.Split('\0');
            if (fields[fields.Length - 1].Length != 0 || (fields.Length - 1) % 2 != 0)
            {
                return false;
            }

            for (int i = 0; i < fields.Length - 1; i += 2)
            {
                if (entries.Count == MaxEntries)
                {
                    return false;
                }

                string scope = fields[i];
                if (!new[] { "system", "global", "local", "worktree", "command" }.Contains(scope, StringComparer.Ordinal))
                {
                    return false;
                }

                string field = fields[i + 1];
                int separator = field.IndexOf('\n');
                string key = separator < 0 ? field : field.Substring(0, separator);
                string value = separator < 0 ? null : field.Substring(separator + 1);
                if (!key.StartsWith("credential.", StringComparison.Ordinal))
                {
                    return false;
                }

                int lastDot = key.LastIndexOf('.');
                string name = key.Substring(lastDot + 1);
                if (name != "helper" && name != "usehttppath" && !EnvironmentSettings.ContainsKey(name))
                {
                    return false;
                }

                entries.Add(new Entry(name, lastDot == 10 ? null : key.Substring(11, lastDot - 11), value, scope));
            }

            return true;
        }

        internal static void AddSettings(EventMetadata metadata, List<Entry> entries, CredentialUrl url, Dictionary<string, string> environment)
        {
            List<string> helpers = new List<string>();
            List<string> operations = new List<string>();
            string useHttpPath = "false";
            foreach (Entry entry in entries.Where(e => e.Matches))
            {
                if (entry.Name == "helper")
                {
                    string helper = DescribeHelper(entry.Value);
                    operations.Add(entry.Scope + ":" + helper);
                    if (helper == "reset")
                    {
                        helpers.Clear();
                    }
                    else
                    {
                        helpers.Add(helper);
                    }
                }
                else if (entry.Name == "usehttppath")
                {
                    useHttpPath = entry.Value == null ? "omitted:valueless" : DescribeValue(entry.Name, entry.Value);
                }
            }

            metadata["HelperChain"] = string.Join(", ", helpers);
            metadata["HelperOperations"] = string.Join(", ", operations);
            metadata["HelperState"] = operations.Count == 0 ? "absent" : helpers.Count == 0 ? "reset-empty" : "configured";
            metadata["CredentialConfigState"] = entries.Any(e => e.Matches && e.Value == null) ?
                "invalid-valueless-setting" : useHttpPath == "omitted:invalid" ? "invalid-useHttpPath" : "captured";
            metadata["UseHttpPath"] = useHttpPath;
            metadata["SystemScopeObserved"] = entries.Any(e => e.Scope == "system");
            bool gcmScopesAvailable = (useHttpPath == "true" || useHttpPath == "false") &&
                Uri.TryCreate(url.Format(), UriKind.Absolute, out _);
            metadata["GcmPreferenceResolution"] = gcmScopesAvailable ?
                "config-and-environment;external-defaults-not-inspected" : "environment-only;config-scope-unavailable";
            Entry gitInteractive = entries.LastOrDefault(e => e.Name == "interactive" && e.Url == null);
            metadata["GitInteractive"] = gitInteractive == null ? "unset" :
                gitInteractive.Value == null ? "omitted:valueless" :
                gitInteractive.Value == "never" ? "never" : DescribeValue("usehttppath", gitInteractive.Value);
            foreach (KeyValuePair<string, string> setting in EnvironmentSettings)
            {
                string value;
                string source;
                if (environment.TryGetValue(setting.Value, out value))
                {
                    source = "environment";
                }
                else if (!gcmScopesAvailable)
                {
                    metadata["Gcm_" + setting.Key] = "unavailable";
                    metadata["Gcm_" + setting.Key + "_Source"] = "unavailable";
                    continue;
                }
                else
                {
                    Entry selected = null;
                    foreach (string scope in GetGcmScopes(url, useHttpPath == "true"))
                    {
                        selected = entries.LastOrDefault(e => e.Name == setting.Key && e.Url == scope);
                        if (selected != null)
                        {
                            break;
                        }
                    }

                    selected = selected ?? entries.LastOrDefault(e => e.Name == setting.Key && e.Url == null);
                    value = selected?.Value;
                    source = selected == null ? "unset" : selected.Url == null ? selected.Scope : "url-scoped-" + selected.Scope;
                }

                metadata["Gcm_" + setting.Key] = value == null && source != "unset" ? "omitted:valueless" : DescribeValue(setting.Key, value);
                metadata["Gcm_" + setting.Key + "_Source"] = source;
            }
        }

        private static string DescribeValue(string name, string value)
        {
            if (value == null)
            {
                return "unset";
            }

            string lower = value.ToLowerInvariant();
            if (name == "interactive" && lower.Length == 0)
            {
                return "true";
            }

            if (name == "azreposcredentialtype")
            {
                return lower == "oauth" || lower == "pat" ? lower : "omitted:invalid";
            }

            if (name == "interactive" && new[] { "auto", "never", "always", "force" }.Contains(lower))
            {
                return lower;
            }

            if (lower == "true" || lower == "yes" || lower == "on" || lower == "1")
            {
                return "true";
            }

            if (lower == "false" || lower == "no" || lower == "off" || lower == "0" || lower == string.Empty)
            {
                return "false";
            }

            if (name == "usehttppath" && long.TryParse(lower, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number))
            {
                return number == 0 ? "false" : "true";
            }

            return "omitted:invalid";
        }

        private static IEnumerable<string> GetGcmScopes(CredentialUrl url, bool useHttpPath)
        {
            if (!Uri.TryCreate(url.Format(), UriKind.Absolute, out Uri uri))
            {
                yield break;
            }

            string host = uri.Host + (uri.IsDefaultPort ? string.Empty : ":" + uri.Port);
            string path = useHttpPath ? uri.AbsolutePath.Trim('/') : string.Empty;
            while (path.Length > 0)
            {
                yield return uri.Scheme + "://" + host + "/" + path;
                yield return host + "/" + path;
                int slash = path.LastIndexOf('/');
                path = slash < 0 ? string.Empty : path.Substring(0, slash);
            }

            while (true)
            {
                yield return uri.Scheme + "://" + host;
                yield return host;
                int dot = host.IndexOf('.');
                if (dot < 0 || host.IndexOf('.', dot + 1) < 0)
                {
                    break;
                }

                host = host.Substring(dot + 1);
            }
        }

        private static bool TryApplyMatches(string output, List<Entry> entries)
        {
            foreach (string field in output.Split('\0'))
            {
                if (field.Length == 0)
                {
                    continue;
                }

                const string prefix = "authdiagnostic.entry";
                int newline = field.IndexOf('\n');
                if (!field.StartsWith(prefix, StringComparison.Ordinal) || newline < prefix.Length ||
                    !int.TryParse(field.Substring(prefix.Length, newline - prefix.Length), out int index) ||
                    index < 0 || index >= entries.Count || field.Substring(newline + 1) != "true")
                {
                    return false;
                }

                entries[index].Matches = true;
            }

            return true;
        }

        private static string EscapeConfig(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\t", "\\t").Replace("\b", "\\b");
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static string RedactUserInfo(string value, string username, string targetUsername)
        {
            int schemeEnd = value.IndexOf("://", StringComparison.Ordinal);
            int authority = schemeEnd < 0 ? 0 : schemeEnd + 3;
            int at = value.IndexOf('@', authority);
            int path = value.IndexOfAny(new[] { '/', '?', '#' }, authority);
            if (at < 0 || (path >= 0 && at >= path))
            {
                return value;
            }

            for (int i = authority; i < at; ++i)
            {
                if (value[i] == '%' && !Uri.IsHexEncoding(value, i))
                {
                    return null;
                }
            }

            string replacement = string.IsNullOrEmpty(username) ? string.Empty :
                username == targetUsername ? "authdiagnostic-user" : "authdiagnostic-other";
            return value.Substring(0, authority) + replacement + value.Substring(at);
        }

        private static Process CreateMatcher(Func<string, Process> createProcess, Dictionary<string, string> environment, string url)
        {
            Process process = createProcess("config --file - --no-includes --null --get-urlmatch authdiagnostic");
            process.StartInfo.Arguments += " " + QuoteArgument(url);
            CopyEnvironment(environment, process.StartInfo);
            // Overrides already appear in the captured entries, not in this synthetic section.
            foreach (string key in process.StartInfo.Environment.Keys.Where(k => k.StartsWith("GIT_CONFIG", StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                process.StartInfo.Environment.Remove(key);
            }

            return process;
        }

        private static void CopyEnvironment(Dictionary<string, string> environment, ProcessStartInfo startInfo)
        {
            startInfo.Environment.Clear();
            foreach (KeyValuePair<string, string> pair in environment)
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }
        }

        internal sealed class Entry
        {
            internal Entry(string name, string url, string value, string scope)
            {
                this.Name = name;
                this.Url = url;
                this.Value = value;
                this.Scope = scope;
            }

            internal string Name { get; }
            internal string Url { get; }
            internal string Value { get; }
            internal string Scope { get; }
            internal bool Matches { get; set; }
        }

        internal sealed class CredentialUrl
        {
            internal string Scheme { get; private set; }
            internal string Host { get; private set; }
            internal string Username { get; private set; }
            internal string Path { get; private set; }

            internal static bool TryParse(string value, out CredentialUrl url)
            {
                url = new CredentialUrl();
                if (value == null || value.Length > 8192 || value.Any(char.IsControl))
                {
                    return false;
                }

                int schemeEnd = value.IndexOf("://", StringComparison.Ordinal);
                string remainder = schemeEnd < 0 ? value : value.Substring(schemeEnd + 3);
                url.Scheme = schemeEnd > 0 ? value.Substring(0, schemeEnd) : null;
                int slash = remainder.IndexOfAny(new[] { '/', '?', '#' });
                string authority = slash < 0 ? remainder : remainder.Substring(0, slash);
                int at = authority.IndexOf('@');
                if (at >= 0)
                {
                    string user = authority.Substring(0, at);
                    int colon = user.IndexOf(':');
                    url.Username = Uri.UnescapeDataString(colon < 0 ? user : user.Substring(0, colon));
                    authority = authority.Substring(at + 1);
                }

                url.Host = authority.Length == 0 ? null : Uri.UnescapeDataString(authority);
                string path = slash < 0 ? string.Empty : remainder.Substring(slash).Trim('/');
                url.Path = path.Length == 0 ? null : Uri.UnescapeDataString(path);
                while (url.Path != null && url.Path.Length > 1 && url.Path.EndsWith("/", StringComparison.Ordinal))
                {
                    url.Path = url.Path.Substring(0, url.Path.Length - 1);
                }

                return new[] { url.Scheme, url.Host, url.Username, url.Path }.All(s => s == null || !s.Any(c => c == '\0' || c == '\r' || c == '\n'));
            }

            internal bool Matches(CredentialUrl other)
            {
                return (this.Scheme == null || this.Scheme == other.Scheme) &&
                    (this.Host == null || this.Host == other.Host) &&
                    (this.Username == null || this.Username == other.Username) &&
                    (this.Path == null || this.Path == other.Path);
            }

            internal string Format(bool redactUsername = false)
            {
                return this.Scheme + "://" +
                    (string.IsNullOrEmpty(this.Username) ? string.Empty :
                        (redactUsername ? "authdiagnostic-user" : Uri.EscapeDataString(this.Username)) + "@") +
                    Uri.EscapeDataString(this.Host ?? string.Empty).Replace("%3A", ":").Replace("%5B", "[").Replace("%5D", "]") +
                    (this.Path == null ? string.Empty : "/" + string.Join("/", this.Path.Split('/').Select(Uri.EscapeDataString)));
            }
        }
    }
}
