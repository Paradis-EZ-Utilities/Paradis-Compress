namespace ParadisCompress.Core.Planning;

public enum TargetVerdict
{
    /// <summary>Enough bits for the chosen size and frame rate.</summary>
    Ok,

    /// <summary>Workable, but detail will visibly suffer. Shown as a note.</summary>
    Low,

    /// <summary>So few bits that text will probably be unreadable. The user must confirm before starting.</summary>
    TooLow,

    /// <summary>The target can't even hold the audio and container. Can't start.</summary>
    Impossible,
}

/// <summary>
/// How a size target turned into bitrates, and whether that's sensible. All sizes are decimal
/// (1 MB = 1,000,000 bytes). <see cref="Message"/> is the full explanation (dialogs, help);
/// <see cref="ShortMessage"/> is the one line the main window shows next to the target.
/// </summary>
public sealed record TargetAssessment(
    TargetVerdict Verdict,
    long TargetBytes,
    long VideoBitsPerSecond,
    int AudioKbps,
    double BitsPerPixel,
    double SuggestedMinimumMB,
    string Message,
    string ShortMessage = "",
    // The size the encoder actually aims for (target minus headroom), in bytes.
    long AimBytes = 0,
    // The target is at least as big as the original file, so the result may not save space.
    bool NotSmallerThanSource = false);

/// <summary>Everything decided about one job, before any process is started.</summary>
public sealed record EncodePlan
{
    public required OutputMode Mode { get; init; }
    public required PresetDefinition Preset { get; init; }
    public required TimeSpan Duration { get; init; }
    public string Extension => Mode == OutputMode.AudioOnly ? ".mp3" : ".mp4";

    // ---- video ----
    public int Width { get; init; }
    public int Height { get; init; }

    /// <summary>The picture is resized (resolution limit, or non-square pixels made square).</summary>
    public bool Scaled { get; init; }

    /// <summary>An odd source width/height got one black pixel row/column so H.264 4:2:0 can take it. Nothing is cropped.</summary>
    public bool PaddedToEven { get; init; }

    /// <summary>New constant frame rate, or null to keep the source's frame timing as it is.</summary>
    public double? OutputFps { get; init; }

    /// <summary>The frame rate the output will have on average (for estimates and the GOP length).</summary>
    public double EffectiveFps { get; init; }

    public int? Crf { get; init; }
    public long? VideoBitsPerSecond { get; init; }
    public bool TwoPass => VideoBitsPerSecond != null && Mode == OutputMode.Video;
    public int KeyframeInterval { get; init; }

    // ---- audio ----
    public bool IncludeAudio { get; init; }
    public int AudioKbps { get; init; }
    public int AudioChannels { get; init; }

    /// <summary>Resample to this rate, or null to keep the source rate.</summary>
    public int? AudioSampleRate { get; init; }

    public TargetAssessment? Target { get; init; }

    /// <summary>Plain-language notes for the user (frame rate lowered, odd size padded, file may grow…).</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>A rough output size for the disk-space check; deliberately on the generous side.</summary>
    public long EstimatedBytes { get; init; }

    /// <summary>One line for the log: every setting that was actually used.</summary>
    public string Describe()
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        if (Mode == OutputMode.AudioOnly)
            return string.Format(ci, "Audio only: MP3 {0} kbit/s CBR, {1} ch{2}{3}", AudioKbps, AudioChannels,
                AudioSampleRate is int sr ? $", {sr} Hz" : "", Target != null ? $", target {Target.TargetBytes} bytes" : "");
        string rate = TwoPass
            ? string.Format(ci, "2-pass {0:0} kbit/s (target {1} bytes)", VideoBitsPerSecond / 1000.0, Target?.TargetBytes)
            : $"CRF {Crf}";
        return string.Format(ci,
            "{0}: H.264 {1}x{2}{3}{4}, fps {5}, x264 preset {6}, {7}, keyint {8}; audio {9}",
            Preset.Name, Width, Height, Scaled ? " (scaled)" : "", PaddedToEven ? " (padded to even)" : "",
            OutputFps is double f ? f.ToString("0.###", ci) : "unchanged", Preset.X264Preset, rate, KeyframeInterval,
            IncludeAudio ? $"AAC {AudioKbps} kbit/s {AudioChannels} ch" + (AudioSampleRate is int r ? $" {r} Hz" : "") : "none");
    }
}
