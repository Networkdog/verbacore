using System.Text.Json.Serialization;

namespace VerbaCore.Models;

/// <summary>
/// Source-generated JSON context for AppSettings serialization.
/// Eliminates reflection overhead for settings load/save.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppSettings))]
internal partial class SettingsJsonContext : JsonSerializerContext;

/// <summary>
/// Source-generated JSON context for LookupHistory serialization.
/// Eliminates reflection overhead for history load/save.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LookupHistory))]
internal partial class HistoryJsonContext : JsonSerializerContext;

/// <summary>
/// Source-generated JSON context for the auto-update manifest (latest.json).
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(UpdateInfo))]
internal partial class UpdateJsonContext : JsonSerializerContext;

/// <summary>
/// Source-generated JSON context for LookupCache serialization.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LookupCache))]
internal partial class CacheJsonContext : JsonSerializerContext;
