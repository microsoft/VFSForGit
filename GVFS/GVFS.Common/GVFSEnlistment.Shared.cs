using GVFS.Common.Tracing;
using System;
using System.Security;

namespace GVFS.Common
{
    public partial class GVFSEnlistment
    {
        private static bool? isUnAttendedByDefault;
        public static void SetIsUnattendedByDefault(bool defaultValue)
        {
            if (isUnAttendedByDefault.HasValue)
            {
                throw new InvalidOperationException("You can call this method only once");
            }

            isUnAttendedByDefault = defaultValue;
        }

        public static bool IsUnattended(ITracer tracer)
        {
            try
            {
                string value = Environment.GetEnvironmentVariable(GVFSConstants.UnattendedEnvironmentVariable);
                if (string.IsNullOrEmpty(value))
                {
                    return isUnAttendedByDefault.GetValueOrDefault(false);
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
                    tracer.RelatedError(metadata, "Unable to read environment variable " + GVFSConstants.UnattendedEnvironmentVariable);
                }

                return false;
            }
        }
    }
}
