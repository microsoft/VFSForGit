using System;
using System.Text.Json;

namespace GVFS.Common
{
    /// <summary>
    /// Shared JsonSerializerOptions and helpers for the GVFS codebase.
    /// PropertyNameCaseInsensitive preserves backward compatibility with
    /// Newtonsoft.Json's default case-insensitive deserialization.
    /// </summary>
    public static class GVFSJsonOptions
    {
        public static readonly JsonSerializerOptions Default = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new VersionConverter(), new Tracing.EventMetadataConverter() },
        };

        /// <summary>
        /// Serialize using the compile-time type. Use when <typeparamref name="T"/>
        /// is the concrete type (not a base class with derived properties).
        /// </summary>
        public static string Serialize<T>(T value)
        {
            return JsonSerializer.Serialize(value, Default);
        }

        /// <summary>
        /// Serialize using the runtime type. Use when calling from a base-class
        /// method where compile-time type would lose derived-class properties
        /// (e.g., BaseResponse&lt;T&gt;.ToMessage()).
        /// </summary>
        public static string Serialize(object value, Type inputType)
        {
            return JsonSerializer.Serialize(value, inputType, Default);
        }

        public static T Deserialize<T>(string json)
        {
            return JsonSerializer.Deserialize<T>(json, Default);
        }
    }
}
