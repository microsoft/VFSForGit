namespace RGFS.Common.Git
{
    public class RefLogEntry
    {
        public RefLogEntry(string sourceSha, string targetSha, string reason)
        {
            this.SourceSha = sourceSha;
            this.TargetSha = targetSha;
            this.Reason = reason;
        }

        public string SourceSha { get; }
        public string TargetSha { get; }
        public string Reason { get; }
        
        public static bool TryParse(string line, out RefLogEntry entry)
        {
            entry = null;
            if (string.IsNullOrEmpty(line))
            {
                return false;
            }

            if (line.Length < RGFSConstants.ShaStringLength + 1 + RGFSConstants.ShaStringLength)
            {
                return false;
            }
            
            string sourceSha = line.Substring(0, RGFSConstants.ShaStringLength);
            string targetSha = line.Substring(RGFSConstants.ShaStringLength + 1, RGFSConstants.ShaStringLength);

            int reasonStart = line.LastIndexOf("\t");
            if (reasonStart < 0)
            {
                return false;
            }

            string reason = line.Substring(reasonStart + 1);

            entry = new RefLogEntry(sourceSha, targetSha, reason);
            return true;
        }
    }
}
