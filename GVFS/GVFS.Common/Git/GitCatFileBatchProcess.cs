using GVFS.Common.Physical.FileSystem;
using GVFS.Common.Physical.Git;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GVFS.Common.Git
{
    public class GitCatFileBatchProcess : GitCatFileProcess
    {
        private const int BufferSize = 64 * 1024;
        private static readonly HashSet<string> ValidBlobModes = new HashSet<string>() { "100644", "100755", "120000" };

        public GitCatFileBatchProcess(ITracer tracer, Enlistment enlistment) : base(tracer, enlistment, "--batch")
        {
        }

        public GitCatFileBatchProcess(StreamReader stdOut, StreamWriter stdIn) : base(stdOut, stdIn)
        {
        }

        public IEnumerable<GitTreeEntry> GetTreeEntries_CanTimeout(string commitId, string path)
        {
            IEnumerable<string> foundShas;
            if (this.TryGetShasForPath_CanTimeout(commitId, path, isFolder: true, shas: out foundShas))
            {
                HashSet<string> alreadyAdded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                List<GitTreeEntry> results = new List<GitTreeEntry>();
                foreach (string sha in foundShas)
                {
                    foreach (GitTreeEntry entry in this.GetTreeEntries_CanTimeout(sha))
                    {
                        if (alreadyAdded.Add(entry.Name))
                        {
                            results.Add(entry);
                        }
                    }
                }

                return results;
            }

            return new GitTreeEntry[0];
        }

        public IEnumerable<GitTreeEntry> GetTreeEntries_CanTimeout(string sha)
        {
            string header;
            char[] rawTreeChars;
            this.StdIn.Write(sha + "\n");
            if (!this.TryReadCatFileBatchOutput_CanTimeout(out header, out rawTreeChars) || 
                !header.Contains(GitCatFileProcess.TreeMarker))
            {
                return new GitTreeEntry[0];
            }

            return this.ParseTree(new string(rawTreeChars));
        }

        public string GetTreeSha_CanTimeout(string commitish)
        {
            string header;
            char[] rawTreeChars;
            this.StdIn.Write(commitish + "\n");
            if (!this.TryReadCatFileBatchOutput_CanTimeout(out header, out rawTreeChars) || 
                !header.Contains(GitCatFileProcess.CommitMarker))
            {
                return null;
            }

            const string TreeLinePrefix = "tree ";
            string commitDetails = new string(rawTreeChars);
            string[] detailLines = commitDetails.Split('\n');
            if (detailLines.Length < 1 || !detailLines[0].StartsWith(TreeLinePrefix))
            {
                throw new InvalidDataException("'tree' expected on first line of 'git cat-file'. Actual: " + (detailLines.Length == 0 ? "empty" : detailLines[0]));
            }

            return detailLines[0].Substring(TreeLinePrefix.Length);
        }

        public bool TryGetFileSha_CanTimeout(string commitId, string virtualPath, out string sha)
        {
            sha = null;

            IEnumerable<string> foundShas;
            if (this.TryGetShasForPath_CanTimeout(commitId, virtualPath, isFolder: false, shas: out foundShas))
            {
                if (foundShas.Count() > 1)
                {
                    return false;
                }

                sha = foundShas.Single();
                return true;
            }

            return false;
        }

        public bool TryCopyBlobContentStream_CanTimeout(string blobSha, Action<StreamReader, long> writeAction)
        {
            string header;
            long blobSize;
            this.StdIn.Write(blobSha + "\n");
            header = this.StdOutCanTimeout.ReadLineAsync().Timeout<string, CopyBlobContentTimeoutException>(GitCatFileProcess.ProcessReadTimeoutMs);
            if (!this.TryParseSizeFromCatFileHeader(header, out blobSize))
            {
                return false;
            }
            
            if (!header.Contains(GitCatFileProcess.BlobMarker))
            {
                // Even if not a blob, be sure to read the remaining bytes (+ 1 for \n) to leave the process in a good state
                this.StdOutCanTimeout.CopyBlockTo<CopyBlobContentTimeoutException>(StreamWriter.Null, blobSize + 1);
                return false;
            }

            writeAction(this.StdOutCanTimeout, blobSize);
            this.StdOutCanTimeout.CopyBlockTo<CopyBlobContentTimeoutException>(StreamWriter.Null, 1);
            return true;
        }

        private bool TryReadCatFileBatchOutput_CanTimeout(out string header, out char[] str)
        {
            long remainingSize;

            header = this.StdOutCanTimeout.ReadLineAsync().Timeout<string, CatFileTimeoutException>(GitCatFileProcess.ProcessReadTimeoutMs);
            if (!this.TryParseSizeFromCatFileHeader(header, out remainingSize))
            {
                str = null;
                return false;
            }

            str = new char[remainingSize + 1]; // Grab trailing \n
            this.StdOutCanTimeout.ReadBlockAsync(str, 0, str.Length).Timeout<CatFileTimeoutException>(GitCatFileProcess.ProcessReadTimeoutMs);

            return true;
        }

        private bool TryParseSizeFromCatFileHeader(string header, out long remainingSize)
        {
            if (header == null || header.EndsWith("missing"))
            {
                remainingSize = 0;
                return false;
            }

            int spaceIdx = header.LastIndexOf(' ');
            if (spaceIdx < 0)
            {
                throw new InvalidDataException("git cat-file has invalid header " + header);
            }

            string sizeString = header.Substring(spaceIdx);
            if (!long.TryParse(sizeString, out remainingSize) || remainingSize < 0)
            {
                remainingSize = 0;
                return false;
            }

            return true;
        }

        private IEnumerable<GitTreeEntry> ParseTree(string rawTreeData)
        {
            int i = 0;
            int len = rawTreeData.Length - 1; // Ingore the trailing \n
            while (i < len)
            {
                int endOfObjMode = rawTreeData.IndexOf(' ', i);
                if (endOfObjMode < 0)
                {
                    throw new InvalidDataException("git cat-file content has invalid mode");
                }

                string objectMode = rawTreeData.Substring(i, endOfObjMode - i);
                bool isBlob = ValidBlobModes.Contains(objectMode);
                i = endOfObjMode + 1; // +1 to skip space

                int endOfObjName = rawTreeData.IndexOf('\0', i);
                if (endOfObjName < 0)
                {
                    throw new InvalidDataException("git cat-file content has invalid name");
                }

                string fileName = Encoding.UTF8.GetString(this.StdOutCanTimeout.CurrentEncoding.GetBytes(rawTreeData.Substring(i, endOfObjName - i)));
                i = endOfObjName + 1; // +1 to skip null

                byte[] shaBytes = this.StdOutCanTimeout.CurrentEncoding.GetBytes(rawTreeData.Substring(i, 20));
                string sha = BitConverter.ToString(shaBytes).Replace("-", string.Empty);
                if (sha.Length != GVFSConstants.ShaStringLength)
                {
                    throw new InvalidDataException("git cat-file content has invalid sha: " + sha);
                }

                i += 20;

                yield return new GitTreeEntry(fileName, sha, !isBlob, isBlob);
            }
        }

        /// <summary>
        /// We are trying to get the sha of a single path.  However, if that is the path of a folder, it can
        /// potentially correspond to multiple git trees, and therefore we have to return multiple shas.
        /// 
        /// This is due to the fact that git and Windows disagree on case sensitivity.  If you add the folders
        /// foo and Foo, git will store those as two different trees, but Windows will only ever create a single
        /// folder that contains the union of the files inside both trees.  In order to enumerate Foo correctly,
        /// we have to treat both trees as if they are the same.
        /// 
        /// This has one major problem, but Git for Windows has the same issue even with no GVFS in the picture.
        /// If you have the files foo\A.txt and Foo\A.txt, after you checkout, git writes both of those files, 
        /// but whichever one gets written second overwrites the one that was written first, and git status
        /// will always report one of them as deleted.  In GVFS, we do a case-insensitive union of foo and Foo,
        /// so we will end up with the same end result.
        /// </summary>
        private bool TryGetShasForPath_CanTimeout(string commitId, string virtualPath, bool isFolder, out IEnumerable<string> shas)
        {
            shas = Enumerable.Empty<string>();

            string rootTreeSha = this.GetTreeSha_CanTimeout(commitId);
            if (rootTreeSha == null)
            {
                return false;
            }

            List<string> currentLevelShas = new List<string>();
            currentLevelShas.Add(rootTreeSha);

            string[] pathParts = virtualPath.Split(new char[] { GVFSConstants.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < pathParts.Length; ++i)
            {
                List<string> nextLevelShas = new List<string>();
                bool isTree = isFolder || i < pathParts.Length - 1;

                foreach (string treeSha in currentLevelShas)
                {
                    IEnumerable<GitTreeEntry> childrenMatchingName =
                        this.GetTreeEntries_CanTimeout(treeSha)
                        .Where(entry =>
                            entry.IsTree == isTree &&
                            string.Equals(pathParts[i], entry.Name, StringComparison.OrdinalIgnoreCase));
                    foreach (GitTreeEntry childEntry in childrenMatchingName)
                    {
                        nextLevelShas.Add(childEntry.Sha);
                    }
                }

                if (nextLevelShas.Count == 0)
                {
                    return false;
                }

                currentLevelShas = nextLevelShas;
            }

            shas = currentLevelShas;
            return true;
        }
    }
}