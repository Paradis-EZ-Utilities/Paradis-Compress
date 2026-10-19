using System.Globalization;
using System.Text.Json;

namespace ParadisCompress.Core.Media;

/// <summary>Turns <c>ffprobe -print_format json -show_format -show_streams</c> output into a <see cref="MediaInfo"/>.</summary>
public static class ProbeParser
{
    public static MediaInfo Parse(string json, string path, long fileSizeBytes)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        VideoStreamInfo? video = null;
        AudioStreamInfo? audio = null;
        int audioCount = 0;
        TimeSpan? videoDuration = null, audioDuration = null;

        if (root.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in streams.EnumerateArray())
            {
                string type = Str(s, "codec_type");
                if (type == "video" && video == null && !IsAttachedPicture(s))
                {
                    video = new VideoStreamInfo
                    {
                        Index = Int(s, "index") ?? 0,
                        Codec = Str(s, "codec_name"),
                        CodedWidth = Int(s, "width") ?? 0,
                        CodedHeight = Int(s, "height") ?? 0,
                        SarNum = Ratio(Str(s, "sample_aspect_ratio"), ':').num,
                        SarDen = Ratio(Str(s, "sample_aspect_ratio"), ':').den,
                        Rotation = ReadRotation(s),
                        AverageFps = Rate(Str(s, "avg_frame_rate")),
                        BaseFps = Rate(Str(s, "r_frame_rate")),
                        BitRate = Long(s, "bit_rate"),
                        PixelFormat = Str(s, "pix_fmt"),
                    };
                    videoDuration = Seconds(s, "duration");
                }
                else if (type == "audio")
                {
                    audioCount++;
                    if (audio == null)
                    {
                        audio = new AudioStreamInfo
                        {
                            Index = Int(s, "index") ?? 0,
                            Codec = Str(s, "codec_name"),
                            Channels = Int(s, "channels") ?? 0,
                            SampleRate = Int(s, "sample_rate") ?? 0,
                            BitRate = Long(s, "bit_rate"),
                        };
                        audioDuration = Seconds(s, "duration");
                    }
                }
            }
        }

        string formatName = "";
        TimeSpan? duration = null;
        if (root.TryGetProperty("format", out var format))
        {
            formatName = Str(format, "format_name");
            duration = Seconds(format, "duration");
        }
        duration ??= videoDuration ?? audioDuration;

        // A stream with no usable size isn't something we can encode (e.g. a broken header).
        if (video != null && (video.CodedWidth <= 0 || video.CodedHeight <= 0)) video = null;
        if (audio != null && audio.Channels <= 0) audio = null;

        return new MediaInfo
        {
            Path = path,
            FileSizeBytes = fileSizeBytes,
            FormatName = formatName,
            Duration = duration is { TotalMilliseconds: > 0 } ? duration : null,
            Video = video,
            Audio = audio,
            AudioStreamCount = audioCount,
        };
    }

    private static bool IsAttachedPicture(JsonElement s) =>
        s.TryGetProperty("disposition", out var d) && (Int(d, "attached_pic") ?? 0) == 1;

    /// <summary>
    /// Rotation lives in the display-matrix side data in modern files and in a "rotate" tag in
    /// older ones. ffprobe reports the matrix angle counter-clockwise (e.g. -90 for a phone held
    /// upright); only whether it's a quarter turn matters for sizing, so it's normalised to 0–270.
    /// </summary>
    internal static int ReadRotation(JsonElement s)
    {
        double? angle = null;
        if (s.TryGetProperty("side_data_list", out var sideData) && sideData.ValueKind == JsonValueKind.Array)
        {
            foreach (var sd in sideData.EnumerateArray())
            {
                if (sd.TryGetProperty("rotation", out var r) && TryNumber(r, out double v))
                {
                    angle = v;
                    break;
                }
            }
        }
        if (angle == null && s.TryGetProperty("tags", out var tags) && tags.TryGetProperty("rotate", out var rot)
            && TryNumber(rot, out double t))
        {
            angle = t;
        }
        if (angle == null) return 0;
        int quarter = (int)Math.Round(angle.Value / 90.0);
        return ((quarter % 4) + 4) % 4 * 90;
    }

    internal static double? Rate(string text)
    {
        var (num, den) = Ratio(text, '/');
        if (num <= 0 || den <= 0) return null;
        return (double)num / den;
    }

    private static (int num, int den) Ratio(string text, char sep)
    {
        if (string.IsNullOrEmpty(text)) return (1, 1);
        int i = text.IndexOf(sep);
        if (i <= 0) return (1, 1);
        bool a = int.TryParse(text.AsSpan(0, i), NumberStyles.Integer, CultureInfo.InvariantCulture, out int num);
        bool b = int.TryParse(text.AsSpan(i + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int den);
        if (!a || !b) return (1, 1);
        // "0:1" means "unknown" in ffprobe's vocabulary; treat as square.
        if (sep == ':' && (num <= 0 || den <= 0)) return (1, 1);
        return (num, den);
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static int? Int(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        return TryNumber(v, out double d) ? (int)d : null;
    }

    private static long? Long(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        return TryNumber(v, out double d) ? (long)d : null;
    }

    private static TimeSpan? Seconds(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v) || !TryNumber(v, out double d)) return null;
        if (double.IsNaN(d) || d <= 0 || d > TimeSpan.MaxValue.TotalSeconds) return null;
        return TimeSpan.FromSeconds(d);
    }

    /// <summary>ffprobe writes most numbers as JSON strings ("1280", "12.480000"); accept both forms.</summary>
    private static bool TryNumber(JsonElement v, out double value)
    {
        value = 0;
        if (v.ValueKind == JsonValueKind.Number) return v.TryGetDouble(out value);
        if (v.ValueKind == JsonValueKind.String)
            return double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        return false;
    }
}
