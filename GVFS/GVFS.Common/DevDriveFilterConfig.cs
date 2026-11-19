using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;

namespace GVFS.Common
{
    public static class DevDriveFilterConfig
    {
        public static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);

        public static bool? ReadClientValue(
            LibGit2Repo.TryReadConfigBoolWithGit readConfig,
            ITracer tracer)
        {
            if (!TryReadValue(readConfig, "enlistment", tracer, out bool? value, out string _))
            {
                return false;
            }

            return value;
        }

        public static bool IsEnabled(
            LibGit2Repo.TryReadConfigBoolWithGit readConfig,
            bool? clientValue,
            string source,
            ITracer tracer,
            out string disabledReason)
        {
            disabledReason = null;
            if (!TryReadValue(readConfig, source, tracer, out bool? value, out disabledReason))
            {
                return false;
            }

            if (value == false || clientValue == false)
            {
                string disabledSource = value == false ? source : "enlistment";
                disabledReason = $"Automatic filter allowance changes are disabled by {GVFSConstants.GitConfig.AutoAllowDevDriveFilter} in the {disabledSource} configuration.";
                return false;
            }

            if ((value ?? GVFSConstants.GitConfig.AutoAllowDevDriveFilterDefault)
                || clientValue == true)
            {
                return true;
            }

            disabledReason = $"Automatic filter allowance changes are not enabled. Set {GVFSConstants.GitConfig.AutoAllowDevDriveFilter} to true to opt in.";
            return false;
        }

        private static bool TryReadValue(
            LibGit2Repo.TryReadConfigBoolWithGit readConfig,
            string source,
            ITracer tracer,
            out bool? value,
            out string error)
        {
            value = null;
            error = "Git is unavailable.";
            if (readConfig == null
                || !readConfig(GVFSConstants.GitConfig.AutoAllowDevDriveFilter, out value, out error))
            {
                error = $"Automatic filter allowance changes are disabled because the {source} configuration read failed. {error}";
                tracer.RelatedWarning(error);
                return false;
            }

            return true;
        }
    }
}
