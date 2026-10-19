using ParadisCompress.Core.Planning;
using ParadisCompress.Core.Media;
using ParadisCompress.Core.Util;

namespace ParadisCompress.Core.Jobs;

/// <summary>
/// Checks a finished file before it gets its real name: it must probe cleanly, have the streams and
/// size we asked for, last as long as the source, and decode without errors at its start and its end.
/// </summary>
public static class OutputValidator
{
    /// <summary>Allowed duration difference: 1 second, or 0.2 % of the length for long recordings.</summary>
    public static TimeSpan DurationTolerance(TimeSpan source) =>
        TimeSpan.FromSeconds(Math.Max(1.0, source.TotalSeconds * 0.002));

    public static async Task<MediaInfo> ValidateAsync(FfmpegTools tools, string outputPath, EncodePlan plan, MediaInfo source, CancellationToken cancel)
    {
        MediaInfo outInfo;
        try
        {
            outInfo = await MediaProber.ProbeAsync(tools, outputPath, cancel).ConfigureAwait(false);
        }
        catch (CompressionException ex) when (ex.Kind != FailureKind.DiskFull)
        {
            throw Fail("the new file can't be read back", ex.Detail);
        }

        var problems = new List<string>();
        if (plan.Mode == OutputMode.Video)
        {
            if (outInfo.Video == null) problems.Add("no video stream");
            else
            {
                if (outInfo.Video.Codec != "h264") problems.Add($"video codec {outInfo.Video.Codec}");
                if (outInfo.Video.DisplayWidth != plan.Width || outInfo.Video.DisplayHeight != plan.Height)
                    problems.Add($"size {outInfo.Video.DisplayWidth}x{outInfo.Video.DisplayHeight}, expected {plan.Width}x{plan.Height}");
            }
            if (plan.IncludeAudio && (outInfo.Audio == null || outInfo.Audio.Codec != "aac")) problems.Add("audio missing");
            if (!plan.IncludeAudio && outInfo.Audio != null) problems.Add("unexpected audio");
        }
        else
        {
            if (outInfo.Audio == null || outInfo.Audio.Codec != "mp3") problems.Add("no MP3 audio");
            if (outInfo.Video != null) problems.Add("unexpected video stream");
        }

        TimeSpan expected = source.Duration ?? plan.Duration;
        TimeSpan actual = outInfo.Duration ?? TimeSpan.Zero;
        if ((actual - expected).Duration() > DurationTolerance(expected))
            problems.Add($"duration {actual.TotalSeconds:0.###}s, expected {expected.TotalSeconds:0.###}s");

        if (problems.Count > 0) throw Fail("the new file doesn't match what was expected", string.Join("; ", problems));

        // Decode the first and last few seconds. Catches truncated or corrupt output without
        // decoding a multi-hour file a second time.
        await DecodeCheckAsync(tools, outputPath, TimeSpan.Zero, cancel).ConfigureAwait(false);
        if (actual.TotalSeconds > 8)
            await DecodeCheckAsync(tools, outputPath, actual - TimeSpan.FromSeconds(4), cancel).ConfigureAwait(false);
        return outInfo;
    }

    private static async Task DecodeCheckAsync(FfmpegTools tools, string path, TimeSpan from, CancellationToken cancel)
    {
        var args = new List<string>
        {
            "-hide_banner", "-nostdin", "-v", "error", "-xerror",
            "-ss", from.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            "-i", FfmpegArguments.FileUrl(path), "-t", "3", "-f", "null", "-",
        };
        var r = await FfmpegProcess.RunAsync(tools.FfmpegPath, args, cancel).ConfigureAwait(false);
        cancel.ThrowIfCancellationRequested();
        if (!r.Succeeded || r.ErrorTail.Count > 0)
            throw Fail("the new file doesn't play back cleanly", r.ErrorText);
    }

    private static CompressionException Fail(string what, string? detail) =>
        new(FailureKind.ValidationFailed, "The result didn't pass its check",
            $"Compression finished, but {what}, so it was not saved. Your original is untouched. Please try again; if it keeps happening, the log has the details.",
            detail);
}
