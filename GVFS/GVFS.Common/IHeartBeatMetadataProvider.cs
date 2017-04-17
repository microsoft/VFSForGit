using GVFS.Common.Tracing;

namespace GVFS.Common
{
    public interface IHeartBeatMetadataProvider
    {
        EventMetadata GetMetadataForHeartBeat();
    }
}
