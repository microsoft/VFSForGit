using GVFS.Common.Tracing;
using System;
using System.Security;

namespace GVFS.Common
{
    public partial class GVFSEnlistment
    {
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
