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
    }
}