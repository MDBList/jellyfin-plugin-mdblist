using System;
using System.Linq;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.MDBList.Library;

/// <summary>
/// Maps Jellyfin media sources/streams to the Trakt values MDBList's
/// /sync/collection takes. Values MDBList doesn't know are left null rather
/// than guessed; it would drop them anyway.
/// </summary>
public static class MediaInfoMapper
{
    /// <summary>
    /// Builds the copy for one media source (version) of an item.
    /// </summary>
    /// <param name="source">The media source.</param>
    /// <returns>The copy, or null when the source has no usable media info.</returns>
    public static MediaCopy? FromSource(MediaSourceInfo source)
    {
        if (string.IsNullOrEmpty(source.Id))
        {
            return null;
        }

        var streams = source.MediaStreams ?? [];
        var video = streams.FirstOrDefault(s => s.Type == MediaStreamType.Video);
        var audio = streams.FirstOrDefault(s => s.Type == MediaStreamType.Audio && s.Index == source.DefaultAudioStreamIndex)
            ?? streams.FirstOrDefault(s => s.Type == MediaStreamType.Audio && s.IsDefault)
            ?? streams.FirstOrDefault(s => s.Type == MediaStreamType.Audio);

        var copy = new MediaCopy
        {
            CopyKey = source.Id,
            Resolution = video is null ? null : Resolution(video.Width, video.Height, video.IsInterlaced),
            Hdr = video is null ? null : Hdr(video.VideoRangeType.ToString()),
            Audio = audio is null ? null : Audio(audio.Codec, audio.Profile),
            AudioChannels = audio is null ? null : Channels(audio.Channels),
            Is3D = source.Video3DFormat is null ? null : true,
        };
        return copy.HasValues ? copy : null;
    }

    /// <summary>
    /// Trakt resolution from a video stream's size. Width decides first, so a
    /// cropped widescreen 1920x800 file still counts as 1080p.
    /// </summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="interlaced">Whether the stream is interlaced.</param>
    /// <returns>The resolution value, or null when the size is unknown.</returns>
    public static string? Resolution(int? width, int? height, bool interlaced)
    {
        var w = width ?? 0;
        var h = height ?? 0;
        if (w <= 0 && h <= 0)
        {
            return null;
        }

        if (w >= 3200 || h >= 1800)
        {
            return "uhd_4k";
        }

        if (w >= 1800 || h >= 1000)
        {
            return interlaced ? "hd_1080i" : "hd_1080p";
        }

        if (w >= 1200 || h >= 700)
        {
            return "hd_720p";
        }

        if (h >= 540)
        {
            return interlaced ? "sd_576i" : "sd_576p";
        }

        return interlaced ? "sd_480i" : "sd_480p";
    }

    /// <summary>
    /// Trakt HDR value from Jellyfin's VideoRangeType name (DOVI, DOVIWithHDR10,
    /// HDR10Plus, HDR10, HLG, SDR, ...); matched by name so new enum members
    /// in later Jellyfin versions degrade to their closest family.
    /// </summary>
    /// <param name="videoRangeType">The VideoRangeType name.</param>
    /// <returns>The HDR value, or null for SDR/unknown.</returns>
    public static string? Hdr(string? videoRangeType)
    {
        var range = videoRangeType ?? string.Empty;
        if (range.StartsWith("DOVI", StringComparison.OrdinalIgnoreCase)
            && !range.Equals("DOVIInvalid", StringComparison.OrdinalIgnoreCase))
        {
            return "dolby_vision";
        }

        if (range.Contains("HDR10Plus", StringComparison.OrdinalIgnoreCase))
        {
            return "hdr10_plus";
        }

        if (range.Contains("HDR10", StringComparison.OrdinalIgnoreCase))
        {
            return "hdr10";
        }

        return range.Contains("HLG", StringComparison.OrdinalIgnoreCase) ? "hlg" : null;
    }

    /// <summary>
    /// Trakt audio value from an audio stream's ffmpeg codec name and profile
    /// (e.g. "truehd" + "Dolby TrueHD + Dolby Atmos", "dts" + "DTS-HD MA").
    /// </summary>
    /// <param name="codec">The codec name.</param>
    /// <param name="profile">The codec profile, if any.</param>
    /// <returns>The audio value, or null when unknown.</returns>
    public static string? Audio(string? codec, string? profile)
    {
        var c = (codec ?? string.Empty).ToLowerInvariant();
        var p = (profile ?? string.Empty).ToLowerInvariant();
        switch (c)
        {
            case "truehd":
                return p.Contains("atmos", StringComparison.Ordinal) ? "dolby_atmos" : "dolby_truehd";
            case "eac3":
                return p.Contains("atmos", StringComparison.Ordinal) ? "dolby_digital_plus_atmos" : "dolby_digital_plus";
            case "ac3":
                return "dolby_digital";
            case "dts":
            case "dca":
                if (p.Contains("dts:x", StringComparison.Ordinal))
                {
                    return "dts_x";
                }

                if (p.Contains("ma", StringComparison.Ordinal))
                {
                    return "dts_ma";
                }

                return p.Contains("hra", StringComparison.Ordinal) || p.Contains("hr", StringComparison.Ordinal) ? "dts_hr" : "dts";
            case "aac":
                return "aac";
            case "mp3":
                return "mp3";
            case "mp2":
                return "mp2";
            case "flac":
                return "flac";
            case "opus":
                return "ogg_opus";
            case "vorbis":
                return "ogg";
            case "wmav2":
            case "wmapro":
                return "wma";
            default:
                return c.StartsWith("pcm", StringComparison.Ordinal) ? "lpcm" : null;
        }
    }

    /// <summary>
    /// Trakt channel layout from a channel count, e.g. 6 -> "5.1".
    /// </summary>
    /// <param name="channels">The channel count.</param>
    /// <returns>The layout, or null when unknown.</returns>
    public static string? Channels(int? channels)
    {
        return channels switch
        {
            1 => "1.0",
            2 => "2.0",
            3 => "2.1",
            4 => "4.0",
            5 => "5.0",
            6 => "5.1",
            7 => "6.1",
            8 => "7.1",
            _ => null,
        };
    }
}
