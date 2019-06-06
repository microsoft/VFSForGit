using GVFS.Common.Tracing;

namespace GVFS.Common
{
    public interface IHeartBeatMetadataProvider
    {
        EventMetadata GetAndResetHeartBeatMetadata(out bool logToFile);
    }
}
