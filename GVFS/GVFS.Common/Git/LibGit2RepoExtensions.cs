using GVFS.Common.Tracing;
using System;
using System.IO;

namespace GVFS.Common.Git
{
    public static class LibGit2RepoExtensions
    {
        public static bool GetConfigBoolOrDefault(this LibGit2Repo repo, ITracer tracer, string key, bool defaultValue)
        {
            ArgumentNullException.ThrowIfNull(repo);
            ArgumentNullException.ThrowIfNull(tracer);
            ArgumentNullException.ThrowIfNull(key);

            try
            {
                return repo.GetConfigBool(key) ?? defaultValue;
            }
            catch (Exception e) when (e is InvalidDataException || e is LibGit2Exception)
            {
                tracer.RelatedWarning($"Failed to read {key} config, using default: {e.Message}");
                return defaultValue;
            }
        }

        public static bool GetConfigBoolOrDefault(ITracer tracer, string repoPath, string key, bool defaultValue)
        {
            ArgumentNullException.ThrowIfNull(tracer);
            ArgumentNullException.ThrowIfNull(repoPath);
            ArgumentNullException.ThrowIfNull(key);

            try
            {
                using (LibGit2Repo repo = new LibGit2Repo(tracer, repoPath))
                {
                    return repo.GetConfigBoolOrDefault(tracer, key, defaultValue);
                }
            }
            catch (Exception e) when (e is InvalidDataException || e is LibGit2Exception)
            {
                tracer.RelatedWarning($"Failed to read {key} config, using default: {e.Message}");
                return defaultValue;
            }
        }
    }
}
