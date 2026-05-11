using GVFS.Common.Tracing;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace GVFS.Common
{
    /// <summary>
    /// Shared JsonSerializerOptions for the GVFS codebase.
    /// Uses source-generated GVFSJsonContext for known types (trim-safe/AOT-safe)
    /// with DefaultJsonTypeInfoResolver as fallback for types not in the context
    /// (e.g., boxed primitives in EventMetadata Dictionary&lt;string, object&gt;).
    /// EventMetadata uses a custom converter that handles Dictionary&lt;string, object&gt;
    /// without reflection, making it NativeAOT compatible.
    /// </summary>
    public static class GVFSJsonOptions
    {
        [UnconditionalSuppressMessage("AOT", "IL2026",
            Justification = "Uses source-gen context for known types; EventMetadataConverter handles Dictionary<string,object> without reflection. DefaultJsonTypeInfoResolver fallback handles boxed primitives in EventMetadata.")]
        [UnconditionalSuppressMessage("AOT", "IL3050",
            Justification = "Uses source-gen context for known types; EventMetadataConverter handles Dictionary<string,object> without reflection. DefaultJsonTypeInfoResolver fallback handles boxed primitives in EventMetadata.")]
        public static readonly JsonSerializerOptions Default = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new VersionConverter(), new EventMetadataConverter() },
            TypeInfoResolverChain = { GVFSJsonContext.Default, new DefaultJsonTypeInfoResolver() },
        };

        [UnconditionalSuppressMessage("AOT", "IL2026",
            Justification = "TypeInfoResolverChain includes GVFSJsonContext (source-gen) + DefaultJsonTypeInfoResolver fallback.")]
        [UnconditionalSuppressMessage("AOT", "IL3050",
            Justification = "TypeInfoResolverChain includes GVFSJsonContext (source-gen) + DefaultJsonTypeInfoResolver fallback.")]
        public static string Serialize<T>(T value)
        {
            return JsonSerializer.Serialize(value, Default);
        }

        [UnconditionalSuppressMessage("AOT", "IL2026",
            Justification = "TypeInfoResolverChain includes GVFSJsonContext (source-gen) + DefaultJsonTypeInfoResolver fallback.")]
        [UnconditionalSuppressMessage("AOT", "IL3050",
            Justification = "TypeInfoResolverChain includes GVFSJsonContext (source-gen) + DefaultJsonTypeInfoResolver fallback.")]
        public static string Serialize(object value, Type inputType)
        {
            return JsonSerializer.Serialize(value, inputType, Default);
        }

        [UnconditionalSuppressMessage("AOT", "IL2026",
            Justification = "TypeInfoResolverChain includes GVFSJsonContext (source-gen) + DefaultJsonTypeInfoResolver fallback.")]
        [UnconditionalSuppressMessage("AOT", "IL3050",
            Justification = "TypeInfoResolverChain includes GVFSJsonContext (source-gen) + DefaultJsonTypeInfoResolver fallback.")]
        public static T Deserialize<T>(string json)
        {
            return JsonSerializer.Deserialize<T>(json, Default);
        }
    }
}
