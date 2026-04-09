using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GVFS.Common.Git
{
    public class GitRefs
    {
        private const string Head = "HEAD\0";
        private const string Master = "master";
        private const string HeadRefPrefix = "refs/heads/";
        private const string TagsRefPrefix = "refs/tags/";
        private const string OriginRemoteRefPrefix = "refs/remotes/origin/";

        private Dictionary<string, string> commitsPerRef;

        private string remoteHeadCommitId = null;

        public GitRefs(IEnumerable<string> infoRefsResponse, string branch)
        {
            // First 4 characters of a given line are the length of the line and not part of the commit id so
            //  skip them (https://git-scm.com/book/en/v2/Git-Internals-Transfer-Protocols)
            this.commitsPerRef =
                infoRefsResponse
                .Where(line =>
                    line.Contains(" " + HeadRefPrefix) ||
                    (line.Contains(" " + TagsRefPrefix) && !line.Contains("^")))
                .Where(line =>
                    branch == null ||
                    line.EndsWith(HeadRefPrefix + branch))
                .Select(line => line.Split(' '))
                .ToDictionary(
                    line => line[1].Replace(HeadRefPrefix, OriginRemoteRefPrefix),
                    line => line[0].Substring(4));

            string lineWithHeadCommit = infoRefsResponse.FirstOrDefault(line => line.Contains(Head));

            if (lineWithHeadCommit != null)
            {
                string[] tokens = lineWithHeadCommit.Split(' ');

                if (tokens.Length >= 2 && tokens[1].StartsWith(Head))
                {
                    // First 8 characters are not part of the commit id so skip them
                    this.remoteHeadCommitId = tokens[0].Substring(8);
                }
            }
        }

        public int Count
        {
            get { return this.commitsPerRef.Count; }
        }

        public string GetTipCommitId(string branch)
        {
            return this.commitsPerRef[OriginRemoteRefPrefix + branch];
        }

        public string GetDefaultBranch()
        {
            IEnumerable<KeyValuePair<string, string>> headRefMatches = this.commitsPerRef.Where(reference =>
                reference.Value == this.remoteHeadCommitId
                && reference.Key.StartsWith(OriginRemoteRefPrefix));

            if (headRefMatches.Count() == 0 || headRefMatches.Count(reference => reference.Key == (OriginRemoteRefPrefix + Master)) > 0)
            {
                // Default to master if no HEAD or if the commit ID or the dafult branch matches master (this is
                //  the same behavior as git.exe)
                return Master;
            }

            // If the HEAD commit ID does not match master grab the first branch that matches
            string defaultBranch = headRefMatches.First().Key;

            if (defaultBranch.Length < OriginRemoteRefPrefix.Length)
            {
                return Master;
            }

            return defaultBranch.Substring(OriginRemoteRefPrefix.Length);
        }

        /// <summary>
        /// Checks if the specified branch exists (case sensitive)
        /// </summary>
        public bool HasBranch(string branch)
        {
            string branchRef = OriginRemoteRefPrefix + branch;
            return this.commitsPerRef.ContainsKey(branchRef);
        }

        public IEnumerable<KeyValuePair<string, string>> GetBranchRefPairs()
        {
            return this.commitsPerRef.Select(kvp => new KeyValuePair<string, string>(kvp.Key, kvp.Value));
        }

        public string ToPackedRefs()
        {
            StringBuilder sb = new StringBuilder();
            const string LF = "\n";

            sb.Append("# pack-refs with: peeled fully-peeled" + LF);
            foreach (string refName in this.commitsPerRef.Keys.OrderBy(key => key))
            {
                sb.Append(this.commitsPerRef[refName] + " " + refName + LF);
            }

            return sb.ToString();
        }
    }
}
