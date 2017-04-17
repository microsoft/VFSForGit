using GVFS.Common.Tracing;
using System.IO;

namespace GVFS.Common.Git
{
    public class GitCatFileBatchCheckProcess : GitCatFileProcess
    {
        public GitCatFileBatchCheckProcess(ITracer tracer, Enlistment enlistment) : base(tracer, enlistment, "--batch-check")
        {
        }

        public GitCatFileBatchCheckProcess(StreamReader stdOut, StreamWriter stdIn) : base(stdOut, stdIn)
        {
        }

        public bool TryGetObjectSize_CanTimeout(string objectSha, out long size)
        {
            this.StdIn.Write(objectSha + "\n");
            string header;
            return this.TryParseSizeFromStdOut_CanTimeout(out header, out size);
        }

        public bool ObjectExists_CanTimeout(string objectSha)
        {
            this.StdIn.Write(objectSha + "\n");
            string header = this.StdOutCanTimeout.ReadLine();
            return header != null && !header.EndsWith("missing");
        }

        private bool TryParseSizeFromStdOut_CanTimeout(out string header, out long size)
        {
            // Git always output at least one \n terminated output, so we cannot hang here
            header = this.StdOutCanTimeout.ReadLineAsync().Timeout<string, CatFileTimeoutException>(GitCatFileProcess.ProcessReadTimeoutMs);

            if (header == null || header.EndsWith("missing"))
            {
                size = 0;
                return false;
            }

            string sizeString = header.Substring(header.LastIndexOf(' '));
            if (!long.TryParse(sizeString, out size) || size < 0)
            {
                throw new InvalidDataException("git cat-file header has invalid size: " + sizeString);
            }

            return true;
        }
    }
}