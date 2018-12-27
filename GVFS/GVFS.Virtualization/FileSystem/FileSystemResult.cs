namespace GVFS.Virtualization.FileSystem
{
    public struct FileSystemResult
    {
        public FileSystemResult(FSResult result, int rawResult)
        {
            this.Result = result;
            this.RawResult = rawResult;
        }

        public FSResult Result { get; }

        /// <summary>
        /// Underlying result. The value of RawResult varies based on the operating system.
        /// </summary>
        public int RawResult { get; }
    }
}
