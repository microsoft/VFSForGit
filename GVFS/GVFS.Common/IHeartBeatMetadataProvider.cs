using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;

namespace GVFS.Common
{
    public interface IHeartBeatMetadataProvider
    {
        EventMetadata GetMetadataForHeartBeat(ref EventLevel eventLevel);
    }
}
