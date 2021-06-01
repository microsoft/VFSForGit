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
        private Verbs commandVerb;

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
                else
                {
                    this.commandVerb = this.StringToVerbs(this.parts[VerbIndex]);
                }
            }
        }

        [Flags]
        public enum Verbs
        {
            Other       = 1 << 0,
            AddOrStage  = 1 << 1,
            Checkout    = 1 << 2,
            Commit      = 1 << 3,
            Move        = 1 << 4,
            Reset       = 1 << 5,
            Status      = 1 << 6,
            UpdateIndex = 1 << 7,
            Restore     = 1 << 8,
            Switch      = 1 << 9,
        }

        public bool IsValidGitCommand
        {
            get { return this.parts != null; }
        }

        public bool IsResetMixed()
        {
            return
                this.IsResetSoftOrMixed() &&
                !this.HasArgument("--soft");
        }

        public bool IsResetSoftOrMixed()
        {
            return
                this.IsVerb(Verbs.Reset) &&
                !this.HasArgument("--hard") &&
                !this.HasArgument("--keep") &&
                !this.HasArgument("--merge");
        }

        public bool IsSerializedStatus()
        {
            return this.IsVerb(Verbs.Status) &&
                this.HasArgumentPrefix("--serialize");
        }

        /// <summary>
        /// This method currently just makes a best effort to detect file paths. Only use this method for optional optimizations
        /// related to file paths. Do NOT use this method if you require a reliable answer.
        /// </summary>
        /// <returns>True if file paths were detected, otherwise false</returns>
        public bool IsCheckoutWithFilePaths()
        {
            if (this.IsVerb(Verbs.Checkout))
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

            if (this.IsVerb(Verbs.Restore))
            {
                return true;
            }

            return false;
        }

        public bool IsVerb(Verbs verbs)
        {
            if (!this.IsValidGitCommand)
            {
                return false;
            }

            return (verbs & this.commandVerb) == this.commandVerb;
        }

        private Verbs StringToVerbs(string verb)
        {
            switch (verb)
            {
                case "add": return Verbs.AddOrStage;
                case "checkout": return Verbs.Checkout;
                case "commit": return Verbs.Commit;
                case "mv": return Verbs.Move;
                case "reset": return Verbs.Reset;
                case "restore": return Verbs.Restore;
                case "stage": return Verbs.AddOrStage;
                case "status": return Verbs.Status;
                case "switch": return Verbs.Switch;
                case "update-index": return Verbs.UpdateIndex;
                default: return Verbs.Other;
            }
        }

        private bool HasArgument(string argument)
        {
            return this.HasAnyArgument(arg => arg == argument);
        }

        private bool HasArgumentPrefix(string argument)
        {
            return this.HasAnyArgument(arg => arg.StartsWith(argument, StringComparison.Ordinal));
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
