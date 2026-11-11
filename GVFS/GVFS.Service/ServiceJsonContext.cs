using System.Text.Json.Serialization;

namespace GVFS.Service
{
    /// <summary>
    /// Source-generated JSON context for GVFS.Service types that cannot be registered
    /// in GVFSJsonContext (GVFS.Common) due to assembly dependency direction.
    /// Required for native AOT where the DefaultJsonTypeInfoResolver reflection
    /// fallback is not available.
    /// </summary>
    [JsonSerializable(typeof(RepoRegistration))]
    [JsonSerializable(typeof(InstallationTelemetryRecord))]
    internal partial class ServiceJsonContext : JsonSerializerContext
    {
    }
}
