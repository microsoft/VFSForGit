using System;
using System.Linq;

namespace GVFS.Common
{
    public class GitCommandLineParser
    {
        private const int GitIndex = 0;
        private const int VerbIndex = 1;
        private const int ArgumentsOffset = 2;

        private readonly string[] parts;

        public GitCommandLineParser(string command)
        {
            if (!string.IsNullOrWhiteSpace(command))
            {
                this.parts = command.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                if (this.parts.Length < VerbIndex + 1 ||
                    this.parts[GitIndex] != "git")
                {
                    this.parts = null;
                }
            }
        }

        public bool IsValidGitCommand
        {
            get { return this.parts != null; }
        }

        public bool IsResetSoftOrMixed()
        {
            return
                this.IsVerb("reset") &&
                !this.HasArgument("--hard") &&
                !this.HasArgument("--keep") &&
                !this.HasArgument("--merge");
        }

        public bool IsResetHard()
        {
            return this.IsVerb("reset") && this.HasArgument("--hard");
        }

        /// <summary>
        /// This method currently just makes a best effort to detect file paths. Only use this method for optional optimizations
        /// related to file paths. Do NOT use this method if you require a reliable answer.
        /// </summary>
        /// <returns>True if file paths were detected, otherwise false</returns>
        public bool IsCheckoutWithFilePaths()
        {
            if (this.IsVerb("checkout"))
            {
                int numArguments = this.parts.Length - ArgumentsOffset;

                // The simplest way to know that we're dealing with file paths is if there are any arguments after a --
                // e.g. git checkout branchName -- fileName
                int dashDashIndex;
                if (this.HasAnyArgument(arg => arg == "--", out dashDashIndex) &&
                    numArguments > dashDashIndex + 1)
                {
                    return true;
                }

                // We also special case one usage with HEAD, as long as there are no other arguments with - or -- that might 
                // result in behavior we haven't tested.
                // e.g. git checkout HEAD fileName
                if (numArguments >= 2 &&
                    !this.HasAnyArgument(arg => arg.StartsWith("-")) &&
                    this.HasArgumentAtIndex(GVFSConstants.DotGit.HeadName, argumentIndex: 0))
                {
                    return true;
                }

                // Note: we have definitely missed some cases of file paths, e.g.:
                //    git checkout branchName fileName (detecting this reliably requires more complicated parsing)
                //    git checkout --patch (we currently have no need to optimize this scenario)
            }

            return false;
        }

        public bool IsVerb(params string[] verbs)
        {
            if (!this.IsValidGitCommand)
            {
                return false;
            }

            return verbs.Any(v => this.parts[VerbIndex] == v);
        }

        private bool HasArgument(string argument)
        {
            return this.HasAnyArgument(arg => arg == argument);
        }

        private bool HasArgumentAtIndex(string argument, int argumentIndex)
        {
            int actualIndex = argumentIndex + ArgumentsOffset;
            return
                this.parts.Length > actualIndex &&
                this.parts[actualIndex] == argument;
        }

        private bool HasAnyArgument(Predicate<string> argumentPredicate)
        {
            int argumentIndex;
            return this.HasAnyArgument(argumentPredicate, out argumentIndex);
        }

        private bool HasAnyArgument(Predicate<string> argumentPredicate, out int argumentIndex)
        {
            argumentIndex = -1;

            if (!this.IsValidGitCommand)
            {
                return false;
            }

            for (int i = ArgumentsOffset; i < this.parts.Length; i++)
            {
                if (argumentPredicate(this.parts[i]))
                {
                    argumentIndex = i - ArgumentsOffset;
                    return true;
                }
            }

            return false;
        }
    }
}
