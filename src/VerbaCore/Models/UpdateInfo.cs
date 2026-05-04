using System.Text.Json.Serialization;

namespace VerbaCore.Models;

/// <summary>
/// Update manifest published as a GitHub Release asset (latest.json).
/// Schema is intentionally minimal so it can be hand-authored if needed.
/// </summary>
public sealed class UpdateInfo
{
    /// <summary>Semantic version of the release (e.g. "0.3.0"). No leading "v".</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>Direct download URL for the installer .exe.</summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    /// <summary>Lower-case hex SHA-256 of the installer .exe.</summary>
    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>ISO-8601 UTC release date.</summary>
    [JsonPropertyName("releaseDate")]
    public string ReleaseDate { get; set; } = string.Empty;

    /// <summary>Human-readable release notes (markdown allowed).</summary>
    [JsonPropertyName("releaseNotes")]
    public string ReleaseNotes { get; set; } = string.Empty;

    /// <summary>If true, the user cannot dismiss the update prompt.</summary>
    [JsonPropertyName("mandatory")]
    public bool Mandatory { get; set; }
}
