using GVFS.Common.Tracing;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace GVFS.Common
{
    public static class GVFSJsonOptions
    {
        [UnconditionalSuppressMessage("AOT", "IL2026",
            Justification = "DefaultJsonTypeInfoResolver fallback handles all types at runtime.")]
        [UnconditionalSuppressMessage("AOT", "IL3050",
            Justification = "DefaultJsonTypeInfoResolver fallback handles all types at runtime.")]
        public static readonly JsonSerializerOptions Default = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new VersionConverter(), new EventMetadataConverter() },
            TypeInfoResolverChain = { GVFSJsonContext.Default, new DefaultJsonTypeInfoResolver() },
        };

        [UnconditionalSuppressMessage("AOT", "IL2026",
            Justification = "TypeInfoResolverChain includes DefaultJsonTypeInfoResolver.")]
        [UnconditionalSuppressMessage("AOT", "IL3050",
            Justification = "TypeInfoResolverChain includes DefaultJsonTypeInfoResolver.")]
        public static string Serialize<T>(T value)
        {
            return JsonSerializer.Serialize(value, Default);
        }

        [UnconditionalSuppressMessage("AOT", "IL2026",
            Justification = "TypeInfoResolverChain includes DefaultJsonTypeInfoResolver.")]
        [UnconditionalSuppressMessage("AOT", "IL3050",
            Justification = "TypeInfoResolverChain includes DefaultJsonTypeInfoResolver.")]
        public static string Serialize(object value, Type inputType)
        {
            return JsonSerializer.Serialize(value, inputType, Default);
        }

        [UnconditionalSuppressMessage("AOT", "IL2026",
            Justification = "TypeInfoResolverChain includes DefaultJsonTypeInfoResolver.")]
        [UnconditionalSuppressMessage("AOT", "IL3050",
            Justification = "TypeInfoResolverChain includes DefaultJsonTypeInfoResolver.")]
        public static T Deserialize<T>(string json)
        {
            return JsonSerializer.Deserialize<T>(json, Default);
        }
    }
}
