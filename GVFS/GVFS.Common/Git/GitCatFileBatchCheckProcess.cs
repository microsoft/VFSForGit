using System.IO;

namespace GVFS.Common.Git
{
    public class GitCatFileBatchCheckProcess : GitCatFileProcess
    {
        public GitCatFileBatchCheckProcess(Enlistment enlistment) : base(enlistment, "--batch-check")
        {
        }

        public GitCatFileBatchCheckProcess(StreamReader stdOut, StreamWriter stdIn) : base(stdOut, stdIn)
        {
        }

        public bool TryGetObjectSize(string objectSha, out long size)
        {
            this.StdIn.Write(objectSha + "\n");
            string header;
            return this.TryParseSizeFromStdOut(out header, out size);
        }

        public bool ObjectExists(string objectSha)
        {
            this.StdIn.Write(objectSha + "\n");
            string header = this.StdOut.ReadLine();
            return header != null && !header.EndsWith("missing");
        }
    }
}