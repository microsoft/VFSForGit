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
        ///
        /// A config read failure (e.g. a broken git invocation, as opposed to the key simply
        /// being absent) is treated the same as "not SHA256" here, matching the established
        /// behavior of GitProcess.TryGetFromConfig elsewhere in this codebase for optional
        /// config reads. Use <see cref="TryIsSha256Repo"/> if the caller wants to distinguish
        /// and surface that failure instead of silently treating it as SHA1.
        /// </summary>
        public static bool IsSha256Repo(GitProcess git)
        {
            TryIsSha256Repo(git, out bool isSha256, out string _);
            return isSha256;
        }

        /// <summary>
        /// Reads extensions.objectformat from the local config of the repo backing the given
        /// GitProcess. Returns false and populates <paramref name="error"/> if the config
        /// could not be read at all (a missing key is not an error - it simply means the
        /// repository defaults to SHA1, and isSha256 is set to false).
        /// </summary>
        public static bool TryIsSha256Repo(GitProcess git, out bool isSha256, out string error)
        {
            GitProcess.ConfigResult result = git.GetFromLocalConfig(ObjectFormatConfigName);
            if (!result.TryParseAsString(out string value, out error))
            {
                isSha256 = false;
                return false;
            }

            isSha256 = IsSha256(value);
            return true;
        }
    }
}
