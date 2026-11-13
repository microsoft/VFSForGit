using GVFS.Common.Git;
using GVFS.Common.Tracing;

namespace GVFS.Common
{
    public static class DevDriveFilterConfig
    {
        public static bool IsEnabled(
            LibGit2Repo.TryReadConfigBoolWithGit readConfig,
            bool? clientValue,
            string source,
            ITracer tracer,
            out string disabledReason)
        {
            disabledReason = null;
            bool? value = null;
            string error = "Git is unavailable.";
            if (readConfig == null
                || !readConfig(GVFSConstants.GitConfig.AutoAllowDevDriveFilter, out value, out error))
            {
                disabledReason = $"Automatic filter allowance changes are disabled because the {source} configuration read failed. {error}";
                tracer.RelatedWarning(disabledReason);
                return false;
            }

            if (!(value ?? GVFSConstants.GitConfig.AutoAllowDevDriveFilterDefault) || clientValue == false)
            {
                string disabledSource = value == false ? source : "enlistment";
                disabledReason = $"Automatic filter allowance changes are disabled by {GVFSConstants.GitConfig.AutoAllowDevDriveFilter} in the {disabledSource} configuration.";
                return false;
            }

            return true;
        }
    }
}
