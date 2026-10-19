namespace ParadisCompress.Core.Planning;

public enum OutputMode
{
    /// <summary>H.264 + AAC in an MP4.</summary>
    Video,

    /// <summary>MP3, sound only.</summary>
    AudioOnly,
}

/// <summary>What the user chose. Null "override" values mean "use the preset's value".</summary>
public sealed record CompressionOptions
{
    public OutputMode Mode { get; init; } = OutputMode.Video;
    public PresetId Preset { get; init; } = PresetId.ClassRecording;

    /// <summary>
    /// Target file size in decimal MB (1 MB = 1,000,000 bytes, so 500 means 500,000,000 bytes).
    /// Null = quality-based encoding, no size target.
    /// </summary>
    public double? TargetSizeMB { get; init; }

    /// <summary>
    /// "Retry smaller": a ceiling on the two-pass video bitrate, set from a previous attempt that came out
    /// above the target. Null = use the bitrate the target works out to. Ignored without a size target.
    /// </summary>
    public long? VideoBitsPerSecondCap { get; init; }

    /// <summary>Advanced: ceiling on the shorter side in pixels. Null = preset default; 0 = keep the original resolution.</summary>
    public int? MaxShortSide { get; init; }

    /// <summary>Advanced: frame-rate ceiling. Null = preset default; 0 = keep the original frame rate.</summary>
    public double? MaxFps { get; init; }

    /// <summary>Advanced: x264 CRF (quality mode only). Null = preset default.</summary>
    public int? Crf { get; init; }

    /// <summary>Mix the sound down to one channel. Off by default; a mono source stays mono either way.</summary>
    public bool MixToMono { get; init; }

    /// <summary>Audio-only: MP3 bitrate in kbit/s. Null = 64 for mono, 96 for stereo.</summary>
    public int? Mp3Kbps { get; init; }
}
