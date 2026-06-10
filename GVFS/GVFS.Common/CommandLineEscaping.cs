using System.Text;

namespace GVFS.Common
{
    /// <summary>
    /// Windows command-line argument escaping per the rules used by
    /// <c>CommandLineToArgvW</c> and the Microsoft C runtime.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When the service spawns a child process via <c>CreateProcessAsUser</c>,
    /// or any code path builds a <c>lpCommandLine</c> string, the receiving
    /// process re-parses that string into <c>argv</c>.  Embedded
    /// <c>"</c> characters that aren't escaped get <em>stripped</em>, which
    /// silently corrupts JSON payloads (e.g. <c>--internal_use_only</c>) and
    /// any other argument that contains quotes.  System.Text.Json (unlike
    /// Newtonsoft.Json) is strict about quoted property names, so the
    /// corruption now manifests as a hard failure.
    /// </para>
    /// <para>
    /// The escaping rules (see
    /// <see href="https://learn.microsoft.com/cpp/cpp/main-function-command-line-args#parsing-c-command-line-arguments"/>):
    /// </para>
    /// <list type="bullet">
    ///   <item>Arguments are separated by whitespace (space or tab).</item>
    ///   <item>A string surrounded by <c>"</c> is treated as one argument
    ///         even if it contains whitespace.</item>
    ///   <item><c>\"</c> is interpreted as a literal <c>"</c>.</item>
    ///   <item>Backslashes are literal <em>unless</em> they immediately
    ///         precede a <c>"</c>: then <c>2n</c> backslashes become <c>n</c>
    ///         backslashes followed by a quote terminator, and <c>2n+1</c>
    ///         backslashes become <c>n</c> backslashes followed by a literal
    ///         <c>"</c>.</item>
    /// </list>
    /// </remarks>
    public static class CommandLineEscaping
    {
        /// <summary>
        /// Escapes a single argument for inclusion in a Windows command line
        /// that will be parsed by <c>CommandLineToArgvW</c> (the default
        /// parser used by the CRT and .NET).
        /// </summary>
        /// <param name="argument">The raw argument value.</param>
        /// <returns>
        /// The escaped argument, including surrounding quotes when needed.
        /// Always quotes when the argument is empty or contains a space,
        /// tab, double-quote, or is otherwise ambiguous to the parser.
        /// </returns>
        public static string EscapeArgument(string argument)
        {
            if (argument == null)
            {
                return "\"\"";
            }

            if (argument.Length > 0 && argument.IndexOfAny(CharactersThatRequireQuoting) < 0)
            {
                return argument;
            }

            StringBuilder builder = new StringBuilder(argument.Length + 2);
            builder.Append('"');

            int i = 0;
            while (i < argument.Length)
            {
                int backslashes = 0;
                while (i < argument.Length && argument[i] == '\\')
                {
                    backslashes++;
                    i++;
                }

                if (i == argument.Length)
                {
                    // Backslashes at the end of the argument: double them so
                    // they don't escape the closing quote we're about to add.
                    builder.Append('\\', backslashes * 2);
                    break;
                }

                if (argument[i] == '"')
                {
                    // Backslashes preceding a literal quote: double them, then
                    // emit \" for the literal quote itself.
                    builder.Append('\\', backslashes * 2 + 1);
                    builder.Append('"');
                }
                else
                {
                    // Backslashes not followed by a quote: emit verbatim.
                    builder.Append('\\', backslashes);
                    builder.Append(argument[i]);
                }

                i++;
            }

            builder.Append('"');
            return builder.ToString();
        }

        private static readonly char[] CharactersThatRequireQuoting = new[] { ' ', '\t', '"' };
    }
}
