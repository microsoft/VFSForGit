using RGFS.Common.Tracing;
using System;
using System.Security;

namespace RGFS.Common
{
    public partial class RGFSEnlistment
    {
        public static bool IsUnattended(ITracer tracer)
        {
            try
            {
                return Environment.GetEnvironmentVariable(RGFSConstants.UnattendedEnvironmentVariable) == "1";
            }
            catch (SecurityException e)
            {
                if (tracer != null)
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Area", nameof(RGFSEnlistment));
                    metadata.Add("Exception", e.ToString());
                    tracer.RelatedError(metadata, "Unable to read environment variable " + RGFSConstants.UnattendedEnvironmentVariable);
                }

                return false;
            }
        }
    }
}
