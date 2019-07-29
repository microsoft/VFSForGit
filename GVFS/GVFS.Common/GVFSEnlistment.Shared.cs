using GVFS.Common.Tracing;
using System;
using System.Security;

namespace GVFS.Common
{
    public partial class GVFSEnlistment
    {
        private static bool? isInteractiveByDefault;
        public static void SetIsInteractiveByDefault(bool defaultValue)
        {
            if (isInteractiveByDefault.HasValue)
            {
                throw new InvalidOperationException("You can call this method only once");
            }

            isInteractiveByDefault = defaultValue;
        }

        public static bool IsInteractive(ITracer tracer)
        {
            try
            {
                string value = Environment.GetEnvironmentVariable(GVFSConstants.InteractiveEnvironmentVariable);
                if (string.IsNullOrEmpty(value))
                {
                    return isInteractiveByDefault.GetValueOrDefault(true);
                }

                return value.Equals("1");
            }
            catch (SecurityException e)
            {
                if (tracer != null)
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Area", nameof(GVFSEnlistment));
                    metadata.Add("Exception", e.ToString());
                    tracer.RelatedError(metadata, "Unable to read environment variable " + GVFSConstants.InteractiveEnvironmentVariable);
                }

                return false;
            }
        }

        public static bool IsUnattended(ITracer tracer)
        {
            try
            {
                return Environment.GetEnvironmentVariable(GVFSConstants.UnattendedEnvironmentVariable) == "1";
            }
            catch (SecurityException e)
            {
                if (tracer != null)
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Area", nameof(GVFSEnlistment));
                    metadata.Add("Exception", e.ToString());
                    tracer.RelatedError(metadata, "Unable to read environment variable " + GVFSConstants.UnattendedEnvironmentVariable);
                }

                return false;
            }
        }
    }
}
