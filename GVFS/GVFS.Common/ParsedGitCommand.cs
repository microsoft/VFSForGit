using System.Collections.Generic;

namespace GVFS.Common
{
    /// <summary>
    /// The result of parsing a Git command line for the working-tree paths it
    /// names. Produced by <see cref="GitPathspecParser"/>.
    /// </summary>
    public class ParsedGitCommand
    {
        private static readonly IReadOnlyList<string> NoPathspecs = new List<string>();

        public ParsedGitCommand(
            string command,
            IReadOnlyList<string> pathspecs,
            string pathspecFromFile,
            bool pathspecFileNul,
            bool failed,
            string changeDirectory,
            string gitDir,
            string workTree)
        {
            this.Command = command;
            this.Pathspecs = pathspecs ?? NoPathspecs;
            this.PathspecFromFile = pathspecFromFile;
            this.PathspecFileNul = pathspecFileNul;
            this.Failed = failed;
            this.ChangeDirectory = changeDirectory;
            this.GitDir = gitDir;
            this.WorkTree = workTree;
        }

        /// <summary>An empty result, returned when there is nothing to parse.</summary>
        public static ParsedGitCommand Empty { get; } =
            new ParsedGitCommand(null, NoPathspecs, null, false, false, null, null, null);

        /// <summary>The Git command (verb), lowercased and with any "git-" prefix removed. Null when absent.</summary>
        public string Command { get; }

        /// <summary>The literal pathspecs the command names, in command-line order.</summary>
        public IReadOnlyList<string> Pathspecs { get; }

        /// <summary>The argument of --pathspec-from-file, if present; "-" means stdin. Null when absent.</summary>
        public string PathspecFromFile { get; }

        /// <summary>True when --pathspec-file-nul was present.</summary>
        public bool PathspecFileNul { get; }

        /// <summary>
        /// True when the command's pathspecs cannot be resolved from the command
        /// line alone, e.g. --pathspec-from-file=- reads them from stdin. Callers
        /// must not treat an empty pathspec set as authoritative when this is set.
        /// </summary>
        public bool Failed { get; }

        /// <summary>The -C value (global change-directory), if present. Null when absent.</summary>
        public string ChangeDirectory { get; }

        /// <summary>The --git-dir value, if present. Null when absent.</summary>
        public string GitDir { get; }

        /// <summary>The --work-tree value, if present. Null when absent.</summary>
        public string WorkTree { get; }

        /// <summary>
        /// True when the command names at least one path (inline, or via
        /// --pathspec-from-file), and the pathspecs were resolvable.
        /// </summary>
        public bool NamesPaths
        {
            get
            {
                return !this.Failed &&
                    (this.Pathspecs.Count > 0 || !string.IsNullOrEmpty(this.PathspecFromFile));
            }
        }
    }
}
