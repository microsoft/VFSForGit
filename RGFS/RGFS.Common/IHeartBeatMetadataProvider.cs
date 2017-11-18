using RGFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;

namespace RGFS.Common
{
    public interface IHeartBeatMetadataProvider
    {
        EventMetadata GetMetadataForHeartBeat(ref EventLevel eventLevel);
    }
}
