using System.Globalization;
using ParadisCompress.Core.Media;

namespace ParadisCompress.Core.Planning;

/// <summary>Builds FFmpeg argument lists (never command strings) from an <see cref="EncodePlan"/>.</summary>
public static class FfmpegArguments
{
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    /// <summary>Common start: quiet logging, machine-readable progress on stdout, never read stdin.</summary>
    private static List<string> Start(string input) =>
    [
        "-hide_banner", "-nostdin", "-loglevel", "level+warning",
        "-progress", "pipe:1", "-nostats", "-stats_period", "0.5",
        "-i", FileUrl(input),
    ];

    /// <summary>
    /// Prefixing "file:" makes FFmpeg treat the path as a plain file whatever it contains: a name like
    /// "lecture%03d.mp4" or "concat:x.mp4" is never read as an image pattern or protocol.
    /// </summary>
    internal static string FileUrl(string path) => "file:" + path;

    /// <param name="pass">0 = single pass (CRF or audio), 1 or 2 = two-pass size targeting.</param>
    /// <param name="passLogPrefix">Where x264 keeps its first-pass statistics (two-pass only).</param>
    public static List<string> Build(EncodePlan plan, MediaInfo info, string input, string output, int pass = 0, string? passLogPrefix = null)
    {
        var a = Start(input);
        if (plan.Mode == OutputMode.AudioOnly)
        {
            a.AddRange(["-map", $"0:{info.Audio!.Index}", "-vn", "-sn", "-dn"]);
            a.AddRange(["-c:a", "libmp3lame", "-b:a", $"{plan.AudioKbps}k", "-ac", plan.AudioChannels.ToString(Ci)]);
            if (plan.AudioSampleRate is int sr) a.AddRange(["-ar", sr.ToString(Ci)]);
            a.AddRange(["-id3v2_version", "3", "-write_xing", "1", "-f", "mp3", "-n", FileUrl(output)]);
            return a;
        }

        var v = info.Video!;
        a.AddRange(["-map", $"0:{v.Index}"]);
        bool withAudio = plan.IncludeAudio && pass != 1;
        if (withAudio) a.AddRange(["-map", $"0:{info.Audio!.Index}"]);

        string filters = VideoFilters(plan);
        if (filters.Length > 0) a.AddRange(["-vf", filters]);

        a.AddRange([
            "-c:v", "libx264",
            "-preset", plan.Preset.X264Preset,
            "-profile:v", "high",
            "-pix_fmt", "yuv420p",
            "-g", plan.KeyframeInterval.ToString(Ci),
        ]);

        if (plan.TwoPass)
        {
            long kbps = Math.Max(1, plan.VideoBitsPerSecond!.Value / 1000);
            a.AddRange(["-b:v", $"{kbps}k", "-pass", pass.ToString(Ci), "-passlogfile", passLogPrefix ?? "ffmpeg2pass"]);
        }
        else
        {
            a.AddRange(["-crf", plan.Crf!.Value.ToString(Ci)]);
        }

        if (withAudio)
        {
            a.AddRange(["-c:a", "aac", "-b:a", $"{plan.AudioKbps}k", "-ac", plan.AudioChannels.ToString(Ci)]);
            if (plan.AudioSampleRate is int sr) a.AddRange(["-ar", sr.ToString(Ci)]);
        }
        else
        {
            a.Add("-an");
        }
        a.AddRange(["-sn", "-dn"]);

        if (pass == 1)
        {
            // The first pass only gathers statistics; the null muxer opens no file at all.
            a.AddRange(["-f", "null", "-"]);
        }
        else
        {
            a.AddRange(["-movflags", "+faststart", "-f", "mp4", "-n", FileUrl(output)]);
        }
        return a;
    }

    /// <summary>
    /// The -vf chain, in order: frame-rate reduction first (less to scale), then resize to square
    /// pixels / resolution limit, or pad an odd size by one pixel. Rotation is applied before this by
    /// FFmpeg's automatic rotation, so the sizes here are already upright.
    /// </summary>
    internal static string VideoFilters(EncodePlan plan)
    {
        var f = new List<string>();
        if (plan.OutputFps is double fps) f.Add("fps=" + FpsText(fps));
        if (plan.Scaled) f.Add($"scale={plan.Width}:{plan.Height}:flags=lanczos");
        else if (plan.PaddedToEven) f.Add($"pad={plan.Width}:{plan.Height}:0:0:color=black");
        if (plan.Scaled || plan.PaddedToEven) f.Add("setsar=1");
        return string.Join(',', f);
    }

    internal static string FpsText(double fps)
    {
        // Keep the exact NTSC rates exact.
        if (Math.Abs(fps - 30000.0 / 1001) < 0.001) return "30000/1001";
        if (Math.Abs(fps - 24000.0 / 1001) < 0.001) return "24000/1001";
        return fps.ToString("0.###", Ci);
    }

    public static List<string> Probe(string input) =>
        ["-hide_banner", "-v", "error", "-print_format", "json", "-show_format", "-show_streams", FileUrl(input)];
}
