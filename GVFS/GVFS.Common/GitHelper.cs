using System;
using System.Linq;

namespace GVFS.Common
{
    public static class GitHelper
    {
        /// <summary>
        /// Determines whether the given command line represents any of the git verbs passed in.
        /// </summary>
        /// <param name="commandLine">The git command line.</param>
        /// <param name="verbs">A list of verbs (eg. "status" not "git status").</param>
        /// <returns>True if the command line represents any of the verbs, false otherwise.</returns>
        public static bool IsVerb(string commandLine, params string[] verbs)
        {
            if (verbs == null || verbs.Length < 1)
            {
                throw new ArgumentException("At least one verb must be provided.", nameof(verbs));
            }

            return 
                verbs.Any(v =>
                {
                    string verbCommand = "git " + v;
                    return
                        commandLine == verbCommand ||
                        commandLine.StartsWith(verbCommand + " ");
                });
        }

        /// <summary>
        /// Returns true if the string is length 40 and all valid hex characters
        /// </summary>
        public static bool IsValidFullSHA(string sha)
        {
            return sha.Length == 40 && !sha.Any(c => !(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f') && !(c >= 'A' && c <= 'F'));
        }
    }
}
