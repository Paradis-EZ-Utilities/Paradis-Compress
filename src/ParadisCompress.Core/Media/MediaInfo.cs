namespace ParadisCompress.Core.Media;

/// <summary>What ffprobe told us about one input file, reduced to what the app needs.</summary>
public sealed record MediaInfo
{
    public required string Path { get; init; }
    public required long FileSizeBytes { get; init; }

    /// <summary>Container name as ffprobe reports it, e.g. "mov,mp4,m4a,3gp,3g2,mj2".</summary>
    public string FormatName { get; init; } = "";

    /// <summary>Overall duration, or null when the container doesn't say (live streams, some broken files).</summary>
    public TimeSpan? Duration { get; init; }

    public VideoStreamInfo? Video { get; init; }
    public AudioStreamInfo? Audio { get; init; }

    /// <summary>How many audio tracks the file has; only the first is used.</summary>
    public int AudioStreamCount { get; init; }

    public bool HasVideo => Video != null;
    public bool HasAudio => Audio != null;
}

public sealed class VideoStreamInfo
{
    /// <summary>Absolute stream index inside the file, used with <c>-map 0:N</c>.</summary>
    public int Index { get; init; }
    public string Codec { get; init; } = "";

    /// <summary>Stored (coded) frame size, before rotation and pixel-aspect correction.</summary>
    public int CodedWidth { get; init; }
    public int CodedHeight { get; init; }

    /// <summary>Sample (pixel) aspect ratio; 1/1 for nearly everything recorded from a screen.</summary>
    public int SarNum { get; init; } = 1;
    public int SarDen { get; init; } = 1;

    /// <summary>Rotation the player must apply, in degrees, normalised to 0, 90, 180 or 270.</summary>
    public int Rotation { get; init; }

    /// <summary>Average frame rate (frames / duration). The honest number for variable-frame-rate files.</summary>
    public double? AverageFps { get; init; }

    /// <summary>"Real base" frame rate from the container. For VFR files this is often a large timebase.</summary>
    public double? BaseFps { get; init; }

    public long? BitRate { get; init; }
    public string PixelFormat { get; init; } = "";

    /// <summary>The frame rate to show and plan with: average when known, else the base rate.</summary>
    public double? Fps => AverageFps is > 0 ? AverageFps : BaseFps is > 0 and < 1000 ? BaseFps : null;

    /// <summary>
    /// True when the base and average rates disagree by more than a few percent, which is how
    /// variable-frame-rate recordings (phones, OBS, Zoom, Game Bar) usually show up.
    /// </summary>
    public bool LooksVariableFrameRate =>
        AverageFps is > 0 && BaseFps is > 0 && Math.Abs(BaseFps.Value - AverageFps.Value) / AverageFps.Value > 0.05;

    /// <summary>Width as it should be displayed: pixel aspect applied, then rotation.</summary>
    public int DisplayWidth => Rotation is 90 or 270 ? SquarePixelHeight : SquarePixelWidth;

    /// <summary>Height as it should be displayed: pixel aspect applied, then rotation.</summary>
    public int DisplayHeight => Rotation is 90 or 270 ? SquarePixelWidth : SquarePixelHeight;

    internal bool HasNonSquarePixels => SarNum > 0 && SarDen > 0 && SarNum != SarDen;

    private int SquarePixelWidth => HasNonSquarePixels
        ? (int)Math.Round(CodedWidth * (double)SarNum / SarDen)
        : CodedWidth;

    private int SquarePixelHeight => CodedHeight;
}

public sealed class AudioStreamInfo
{
    public int Index { get; init; }
    public string Codec { get; init; } = "";
    public int Channels { get; init; }
    public int SampleRate { get; init; }
    public long? BitRate { get; init; }
}
