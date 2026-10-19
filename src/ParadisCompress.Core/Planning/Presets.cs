namespace ParadisCompress.Core.Planning;

public enum PresetId
{
    ClassRecording,
    Balanced,
    SmallestFile,
}

/// <summary>
/// One compression preset. Every value here is passed to FFmpeg exactly as written. The README's
/// preset table lists the same numbers, and <c>PresetTests</c> pins them so the two can't drift silently.
/// </summary>
public sealed record PresetDefinition(
    PresetId Id,
    string Name,
    string Summary,
    string Detail,
    // x264 constant rate factor. Lower = better picture, bigger file. 18 is near-transparent; 23 is x264's default.
    int Crf,
    // x264 speed preset: how hard the encoder searches. Slower = smaller at the same quality.
    string X264Preset,
    // Frame rate ceiling. Sources above it are reduced to it; slower sources are never sped up.
    double MaxFps,
    // Ceiling on the shorter side (1080 = "1080p"). Null = keep the source resolution.
    int? MaxShortSide,
    // AAC bitrate for stereo audio, in kbit/s. Mono gets about two thirds.
    int AudioKbps,
    // Longest gap between keyframes. Scene changes (a new slide) still get one immediately.
    int KeyframeSeconds);

public static class Presets
{
    public static readonly PresetDefinition ClassRecording = new(
        PresetId.ClassRecording,
        "Class Recording",
        "Keeps slides, code and speech clear.",
        "Keeps the original resolution so small text stays sharp. Lowers very smooth video (above 30 fps) to 30 fps, which slides, code and talking don't need. Clear speech audio.",
        Crf: 23, X264Preset: "medium", MaxFps: 30, MaxShortSide: null, AudioKbps: 96, KeyframeSeconds: 10);

    public static readonly PresetDefinition Balanced = new(
        PresetId.Balanced,
        "Balanced",
        "A good all-round trade-off between quality, size and speed.",
        "Limits the picture to 1080p and 30 fps and compresses a little harder. Fine for most videos; very small text from a 4K or 1440p screen may get softer.",
        Crf: 26, X264Preset: "medium", MaxFps: 30, MaxShortSide: 1080, AudioKbps: 96, KeyframeSeconds: 5);

    public static readonly PresetDefinition SmallestFile = new(
        PresetId.SmallestFile,
        "Smallest File",
        "Much smaller files. Small text and fine detail may become hard to read.",
        "Reduces the picture to 720p and 15 fps and compresses hard. Good when you mainly need to hear the lecture and see the general picture. Small code, fine print and fast motion can become blurry or blocky.",
        Crf: 30, X264Preset: "slow", MaxFps: 15, MaxShortSide: 720, AudioKbps: 64, KeyframeSeconds: 10);

    public static IReadOnlyList<PresetDefinition> All { get; } = [ClassRecording, Balanced, SmallestFile];

    public static PresetDefinition Get(PresetId id) => id switch
    {
        PresetId.ClassRecording => ClassRecording,
        PresetId.Balanced => Balanced,
        PresetId.SmallestFile => SmallestFile,
        _ => ClassRecording,
    };
}
