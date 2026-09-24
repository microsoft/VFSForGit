using System;

namespace GVFS.Common.Git
{
    /// <summary>
    /// Detects whether a git repository was initialized with the SHA256 object format
    /// (git's newer, opt-in hash algorithm) rather than the SHA1 format VFS for Git
    /// assumes throughout its object id handling.
    ///
    /// There is no supported way to convert a SHA256 repository to SHA1 in place: an
    /// object's id is a hash of its content plus the algorithm used to compute it, so
    /// "downgrading" would mean rewriting every object with a new identity - effectively
    /// a fresh clone. Callers should use this detection to fail fast with a clear error
    /// rather than let SHA1-only code (e.g. Sha1Id, SHA1Util, index parsing) hit
    /// corruption or an unpredictable crash on 20-byte/40-hex-char assumptions.
    /// </summary>
    public static class ObjectFormat
    {
        public const string ObjectFormatConfigName = "extensions.objectformat";
        public const string Sha256Value = "sha256";

        public const string UnsupportedSha256ErrorMessage =
            "VFS for Git only supports SHA1 repositories, but this repository was initialized " +
            "with the SHA256 object format. There is no supported way to convert a SHA256 " +
            "repository to SHA1 in place. Re-clone or re-initialize the repository as a SHA1 " +
            "repository (for example, 'git init --object-format=sha1').";

        /// <summary>
        /// Returns true if the given value of extensions.objectformat identifies a SHA256
        /// repository. A missing/empty value means the repository defaults to SHA1.
        /// </summary>
        public static bool IsSha256(string objectFormatConfigValue)
        {
            return string.Equals(objectFormatConfigValue?.Trim(), Sha256Value, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Reads extensions.objectformat from the local config of the repo backing the given
        /// GitProcess, and returns true if it identifies a SHA256 repository.
        /// </summary>
        public static bool IsSha256Repo(GitProcess git)
        {
            GitProcess.ConfigResult result = git.GetFromLocalConfig(ObjectFormatConfigName);
            return result.TryParseAsString(out string value, out string _) && IsSha256(value);
        }
    }
}
