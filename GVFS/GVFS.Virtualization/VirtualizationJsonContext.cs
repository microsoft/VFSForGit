using System.Text.Json.Serialization;
using GVFS.Virtualization.Background;

namespace GVFS.Virtualization
{
    [JsonSerializable(typeof(FileSystemTask))]
    internal partial class VirtualizationJsonContext : JsonSerializerContext
    {
    }
}
