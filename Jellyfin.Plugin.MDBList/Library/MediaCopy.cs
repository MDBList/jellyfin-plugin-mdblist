using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Jellyfin.Plugin.MDBList.Library;

/// <summary>
/// Media info of one version (media source) of a collected movie or episode,
/// in the Trakt sync/collection values MDBList's POST /sync/collection takes.
/// A plain settable class (not a record) so the sync state file can
/// round-trip it with its Populate deserialization.
/// </summary>
public class MediaCopy
{
    /// <summary>
    /// Gets or sets the stable id of this version: the Jellyfin media source id.
    /// </summary>
    public string CopyKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the resolution, e.g. "uhd_4k" or "hd_1080p".
    /// </summary>
    public string? Resolution { get; set; }

    /// <summary>
    /// Gets or sets the HDR format: "dolby_vision", "hdr10", "hdr10_plus" or "hlg".
    /// </summary>
    public string? Hdr { get; set; }

    /// <summary>
    /// Gets or sets the audio format, e.g. "dolby_truehd" or "dts_ma".
    /// </summary>
    public string? Audio { get; set; }

    /// <summary>
    /// Gets or sets the audio channels, e.g. "5.1".
    /// </summary>
    public string? AudioChannels { get; set; }

    /// <summary>
    /// Gets or sets whether the version is 3D (null when it isn't).
    /// </summary>
    public bool? Is3D { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the copy was read from its own
    /// library item rather than through a sibling version (whose cached read may
    /// be older). Not saved in the state file and not part of the fingerprint.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsOwn { get; set; }

    /// <summary>
    /// Gets a value indicating whether any media value is known.
    /// </summary>
    public bool HasValues => Resolution is not null || Hdr is not null || Audio is not null || AudioChannels is not null || Is3D is not null;

    /// <summary>
    /// A stable text form of a set of copies, for telling whether an item's
    /// media info changed since it was last pushed (order-independent).
    /// </summary>
    /// <param name="copies">The copies, or null.</param>
    /// <returns>The fingerprint; empty for no copies.</returns>
    public static string Fingerprint(IEnumerable<MediaCopy>? copies)
    {
        if (copies is null)
        {
            return string.Empty;
        }

        return string.Join(
            ';',
            copies
                .OrderBy(c => c.CopyKey, System.StringComparer.Ordinal)
                .Select(c => string.Join('|', c.CopyKey, c.Resolution, c.Hdr, c.Audio, c.AudioChannels, c.Is3D)));
    }

    /// <summary>
    /// The copy as a /sync/collection "copies" entry, leaving out unknown values.
    /// </summary>
    /// <returns>The JSON object.</returns>
    public JsonObject ToJson()
    {
        var json = new JsonObject { ["copy_key"] = CopyKey };
        if (Resolution is not null)
        {
            json["resolution"] = Resolution;
        }

        if (Hdr is not null)
        {
            json["hdr"] = Hdr;
        }

        if (Audio is not null)
        {
            json["audio"] = Audio;
        }

        if (AudioChannels is not null)
        {
            json["audio_channels"] = AudioChannels;
        }

        if (Is3D is not null)
        {
            json["3d"] = Is3D.Value;
        }

        return json;
    }
}
