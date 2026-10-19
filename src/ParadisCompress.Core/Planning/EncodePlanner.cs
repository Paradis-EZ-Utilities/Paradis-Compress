using System.Globalization;
using ParadisCompress.Core.Media;
using ParadisCompress.Core.Util;

namespace ParadisCompress.Core.Planning;

/// <summary>
/// Turns a probed input and the user's choices into an <see cref="EncodePlan"/>: output size, frame
/// rate, rate control, audio settings and, for a size target, the bitrate budget. Pure maths, no I/O.
/// </summary>
public static class EncodePlanner
{
    /// <summary>Share of the target we actually aim for. Two-pass x264 lands within a few percent; this keeps results under the target.</summary>
    public const double TargetHeadroom = 0.96;

    /// <summary>Below this many bits per pixel per frame, text is likely to break up. Calibrated on the screen-recording fixtures.</summary>
    public const double TooLowBitsPerPixel = 0.004;

    /// <summary>Below this, fine detail visibly suffers but text usually survives on mostly-still content.</summary>
    public const double LowBitsPerPixel = 0.01;

    public const long MinimumVideoBitsPerSecond = 100_000;

    /// <summary>MP3 bitrates LAME accepts in CBR mode at 32–48 kHz.</summary>
    public static readonly int[] Mp3Bitrates = [32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320];

    public static EncodePlan Plan(MediaInfo info, CompressionOptions options)
    {
        if (info.Duration is not { } duration || duration.TotalSeconds < 0.05)
            throw FriendlyErrors.InvalidInput("Input has no usable duration.");
        return options.Mode == OutputMode.AudioOnly ? PlanAudio(info, options, duration) : PlanVideo(info, options, duration);
    }

    // ------------------------------------------------------------------ video

    private static EncodePlan PlanVideo(MediaInfo info, CompressionOptions o, TimeSpan duration)
    {
        var v = info.Video ?? throw new CompressionException(FailureKind.InvalidInput, "This file has no picture",
            "There's no video in this file, only sound. Choose \"Extract audio\" instead.");
        var preset = Presets.Get(o.Preset);
        var notes = new List<string>();

        // Size: the rotated, square-pixel display size, limited on the shorter side, never enlarged.
        int dispW = Math.Max(2, v.DisplayWidth), dispH = Math.Max(2, v.DisplayHeight);
        int? limit = o.MaxShortSide switch { null => preset.MaxShortSide, 0 => null, int l => l };
        int w = dispW, h = dispH;
        bool scaled = false, padded = false;
        int shortSide = Math.Min(dispW, dispH);
        if (limit is int lim && shortSide > lim)
        {
            double f = (double)lim / shortSide;
            w = Even(dispW * f);
            h = Even(dispH * f);
            if (dispW <= dispH) w = lim % 2 == 0 ? lim : lim - 1; else h = lim % 2 == 0 ? lim : lim - 1;
            scaled = true;
            notes.Add($"The picture is reduced from {dispW}×{dispH} to {w}×{h}.");
        }
        else if (v.HasNonSquarePixels)
        {
            w = Even(dispW);
            h = Even(dispH);
            scaled = true;
        }
        else if (w % 2 == 1 || h % 2 == 1)
        {
            // Pad (never crop, never resample): every source pixel survives at 1:1, which keeps text crisp.
            w += w % 2;
            h += h % 2;
            padded = true;
        }

        // Frame rate: only ever lowered.
        double? srcFps = v.Fps;
        double? cap = o.MaxFps switch { null => preset.MaxFps, 0 => null, double c => c };
        double? outFps = null;
        if (cap is double cp && srcFps is double sf && sf > cp * 1.05)
        {
            outFps = cp;
            notes.Add(string.Format(CultureInfo.CurrentCulture, "Frame rate is lowered from {0} to {1}.",
                Units.FormatFps(sf), Units.FormatFps(cp)));
        }
        double effFps = outFps ?? srcFps ?? 30;
        if (effFps <= 0 || effFps > 240) effFps = 30;

        // Audio.
        bool audio = info.Audio != null;
        int channels = audio ? (o.MixToMono || info.Audio!.Channels == 1 ? 1 : 2) : 0;
        int audioKbps = audio ? AacKbps(preset.AudioKbps, channels) : 0;
        int? sampleRate = audio && info.Audio!.SampleRate > 48000 ? 48000 : null;
        if (audio && info.AudioStreamCount > 1)
            notes.Add("This file has more than one audio track; only the first is kept.");

        int keyint = Math.Max(1, (int)Math.Round(effFps * preset.KeyframeSeconds));

        TargetAssessment? target = null;
        int? crf = null;
        long? videoBps = null;
        long estimate;
        if (o.TargetSizeMB is double mb)
        {
            target = AssessVideoTarget(mb, duration, w, h, effFps, audio, channels, ref audioKbps, info.FileSizeBytes, sampleRate ?? info.Audio?.SampleRate ?? 48000);
            videoBps = Math.Max(1, target.VideoBitsPerSecond);
            if (o.VideoBitsPerSecondCap is long bpsCap && bpsCap > 0 && bpsCap < videoBps)
            {
                videoBps = bpsCap;
                notes.Add(string.Format(CultureInfo.CurrentCulture,
                    "Retrying at a lower video bitrate ({0:N0} kbit/s) because the last attempt came out above the target.", bpsCap / 1000.0));
            }
            estimate = target.TargetBytes;
        }
        else
        {
            crf = Math.Clamp(o.Crf ?? preset.Crf, 0, 51);
            // A deliberately generous guess (screen content usually needs far less), only for the free-space check.
            double guessBps = w * (double)h * effFps * 0.08 + audioKbps * 1000.0;
            estimate = (long)Math.Min(guessBps / 8 * duration.TotalSeconds, Math.Max(info.FileSizeBytes * 1.2, 50.0 * Units.BytesPerMB));
        }

        if (v.Rotation != 0)
            notes.Add($"The video is turned upright ({v.Rotation}° rotation applied), so it plays the right way round everywhere.");
        if (padded)
            notes.Add("The picture has an odd width or height, so one black pixel row or column is added. Nothing is cropped.");

        return new EncodePlan
        {
            Mode = OutputMode.Video,
            Preset = preset,
            Duration = duration,
            Width = w,
            Height = h,
            Scaled = scaled,
            PaddedToEven = padded,
            OutputFps = outFps,
            EffectiveFps = effFps,
            Crf = crf,
            VideoBitsPerSecond = videoBps,
            KeyframeInterval = keyint,
            IncludeAudio = audio,
            AudioKbps = audioKbps,
            AudioChannels = channels,
            AudioSampleRate = sampleRate,
            Target = target,
            Notes = notes,
            EstimatedBytes = estimate,
        };
    }

    /// <summary>
    /// Bitrate budget for a size target (decimal MB: 500 MB = 500,000,000 bytes).
    /// <code>
    /// budget bits   = target bytes × 8 × 0.96 (headroom for encoder variation)
    /// container     ≈ 4 KB + 20 bytes per video frame + 6 bytes per AAC frame (1024 samples)
    /// audio bits    = AAC kbit/s × 1000 × seconds   (audio is cut to ≤ 25 % of the budget, floor 48/32 kbit/s)
    /// video bit/s   = (budget − container − audio) ÷ seconds
    /// </code>
    /// </summary>
    internal static TargetAssessment AssessVideoTarget(double targetMB, TimeSpan duration, int w, int h, double fps,
        bool hasAudio, int channels, ref int audioKbps, long sourceBytes, int audioSampleRate)
    {
        double secs = duration.TotalSeconds;
        long targetBytes = Units.MBToBytes(targetMB);
        double budgetBits = targetBytes * 8.0 * TargetHeadroom;
        double overheadBits = (4096 + 20.0 * fps * secs + (hasAudio ? 6.0 * audioSampleRate / 1024.0 * secs : 0)) * 8;
        double totalKbps = (budgetBits - overheadBits) / secs / 1000.0;

        if (hasAudio)
        {
            int floor = channels == 1 ? 32 : 48;
            if (audioKbps > 0.25 * totalKbps) audioKbps = Math.Max(floor, (int)(0.25 * totalKbps) / 8 * 8);
        }
        double videoBps = (budgetBits - overheadBits) / secs - audioKbps * 1000.0;
        double bpp = videoBps / (w * (double)h * fps);

        // What size would give a comfortable picture (LowBitsPerPixel) at this resolution and frame rate?
        double comfortableBits = (LowBitsPerPixel * w * h * fps + audioKbps * 1000.0) * secs + overheadBits;
        double suggestedMB = Math.Ceiling(comfortableBits / 8 / TargetHeadroom / Units.BytesPerMB);

        var c = CultureInfo.CurrentCulture;
        string target = Units.FormatTargetMB(targetMB);
        long aimBytes = (long)(targetBytes * TargetHeadroom);
        TargetVerdict verdict;
        string message, shortMessage;
        if (videoBps < 40_000)
        {
            verdict = TargetVerdict.Impossible;
            message = string.Format(c,
                hasAudio
                    ? "{0} is too small for {1} of video: the sound alone needs most of it. Try at least {2:0} MB, or use Extract audio for a sound-only file."
                    : "{0} is too small for {1} of video. Try at least {2:0} MB.",
                target, Units.FormatDuration(duration), suggestedMB);
            shortMessage = string.Format(c, "Too small for {0} of video. Try at least {1:0} MB.", Units.FormatDuration(duration), suggestedMB);
        }
        else if (bpp < TooLowBitsPerPixel || videoBps < MinimumVideoBitsPerSecond)
        {
            verdict = TargetVerdict.TooLow;
            message = string.Format(c,
                "{0} is very small for {1} at {2}×{3}. Text and slides will probably be blurry or blocky. " +
                "For a clearer result, try about {4:0} MB" + (hasAudio ? ", choose a lower resolution under Advanced, or use Extract audio." : " or choose a lower resolution under Advanced."),
                target, Units.FormatDuration(duration), w, h, suggestedMB);
            shortMessage = string.Format(c, "Very small for {0}×{1}: text will likely blur. About {2:0} MB is clearer.", w, h, suggestedMB);
        }
        else if (bpp < LowBitsPerPixel)
        {
            verdict = TargetVerdict.Low;
            message = string.Format(c,
                "This is a tight target. Still pictures should be fine, but movement and fine detail will look softer. About {0:0} MB would look clearly better.",
                suggestedMB);
            shortMessage = string.Format(c, "Tight: detail will soften. About {0:0} MB looks better.", suggestedMB);
        }
        else
        {
            verdict = TargetVerdict.Ok;
            message = string.Format(c,
                "The app aims for about {0} ({1}), a little under the target, and checks the finished file's exact size.",
                Units.FormatSize(aimBytes), Units.FormatBytes(aimBytes));
            shortMessage = string.Format(c, "Aims for about {0}, two-pass.", Units.FormatSize(aimBytes));
        }
        bool notSmaller = verdict != TargetVerdict.Impossible && sourceBytes > 0 && targetBytes >= sourceBytes;
        if (notSmaller)
        {
            message += string.Format(c, " This target is not smaller than the original file ({0}), so the result may not save any space.", Units.FormatSize(sourceBytes));
            shortMessage = string.Format(c, "Not smaller than the original ({0}); may not save space.", Units.FormatSize(sourceBytes));
        }

        return new TargetAssessment(verdict, targetBytes, (long)videoBps, audioKbps, bpp, suggestedMB, message, shortMessage, aimBytes, notSmaller);
    }

    // ------------------------------------------------------------------ audio only

    private static EncodePlan PlanAudio(MediaInfo info, CompressionOptions o, TimeSpan duration)
    {
        var a = info.Audio ?? throw new CompressionException(FailureKind.InvalidInput, "This video has no sound",
            "There's no audio track in this file, so there's nothing to extract.");
        int channels = o.MixToMono || a.Channels == 1 ? 1 : 2;
        int kbps = o.Mp3Kbps ?? (channels == 1 ? 64 : 96);
        kbps = SnapMp3Down(kbps);
        int? sampleRate = a.SampleRate > 48000 ? 48000 : null;
        TargetAssessment? target = null;
        // The UI explains what audio-only leaves out in its own panel, so no note repeats it here.
        var notes = new List<string>();

        if (o.TargetSizeMB is double mb)
        {
            long targetBytes = Units.MBToBytes(mb);
            double budgetKbps = (targetBytes * 8.0 * 0.98 - 64_000) / duration.TotalSeconds / 1000.0;
            int fit = Mp3Bitrates.LastOrDefault(b => b <= budgetKbps);
            double minMB = Math.Ceiling((32_000 * duration.TotalSeconds + 64_000) / 8 / 0.98 / Units.BytesPerMB);
            var c = CultureInfo.CurrentCulture;
            if (fit == 0)
            {
                target = new TargetAssessment(TargetVerdict.Impossible, targetBytes, 0, 0, 0, minMB, string.Format(c,
                    "{0} is too small for {1} of audio. The smallest sensible MP3 needs about {2:0} MB.", Units.FormatTargetMB(mb), Units.FormatDuration(duration), minMB),
                    string.Format(c, "Too small for {0} of audio. Try at least {1:0} MB.", Units.FormatDuration(duration), minMB));
            }
            else
            {
                kbps = Math.Min(kbps, fit);
                long est = Mp3Bytes(kbps, duration);
                var verdict = kbps < (channels == 1 ? 40 : 56) ? TargetVerdict.Low : TargetVerdict.Ok;
                bool notSmaller = info.FileSizeBytes > 0 && targetBytes >= info.FileSizeBytes;
                int chosen = SnapMp3Down(o.Mp3Kbps ?? (channels == 1 ? 64 : 96));
                string shortMessage = verdict == TargetVerdict.Low
                    ? string.Format(c, "MP3 lowered to {0} kbit/s to fit: speech will sound duller.", kbps)
                    : kbps < chosen
                        ? string.Format(c, "MP3 lowered to {0} kbit/s to fit: about {1}.", kbps, Units.FormatSize(est))
                        : string.Format(c, "Fits at the chosen MP3 bitrate, {0} kbit/s: about {1}.", kbps, Units.FormatSize(est));
                if (notSmaller)
                    shortMessage = string.Format(c, "MP3 at {0} kbit/s. Target not smaller than the original.", kbps);
                target = new TargetAssessment(verdict, targetBytes, 0, kbps, 0, minMB,
                    verdict == TargetVerdict.Low
                        ? $"To fit, the MP3 uses {kbps} kbit/s. Speech should stay understandable but will sound duller."
                        : string.Format(c, "The target sets the MP3 bitrate: {0} kbit/s constant bitrate, about {1}, which fits within the target. " +
                            "A larger target can't raise the quality above the MP3 quality chosen under Advanced.", kbps, Units.FormatSize(est)),
                    shortMessage, est, notSmaller);
            }
        }

        return new EncodePlan
        {
            Mode = OutputMode.AudioOnly,
            Preset = Presets.Get(o.Preset),
            Duration = duration,
            IncludeAudio = true,
            AudioKbps = kbps,
            AudioChannels = channels,
            AudioSampleRate = sampleRate,
            Target = target,
            Notes = notes,
            EstimatedBytes = Mp3Bytes(kbps, duration) + 65_536,
        };
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Size of a CBR MP3 with a small tag, in bytes.</summary>
    internal static long Mp3Bytes(int kbps, TimeSpan duration) => (long)(kbps * 1000.0 / 8 * duration.TotalSeconds) + 1_000;

    /// <summary>The next lower MP3 bitrate (for "Retry smaller"), or null at the floor.</summary>
    public static int? NextLowerMp3(int kbps) => Mp3Bitrates.LastOrDefault(b => b < kbps) is int b and > 0 ? b : null;

    internal static int AacKbps(int stereoKbps, int channels) =>
        channels == 1 ? Math.Max(32, (int)Math.Round(stereoKbps * 2 / 3.0 / 8) * 8) : stereoKbps;

    internal static int SnapMp3Down(int kbps) => Mp3Bitrates.LastOrDefault(b => b <= kbps) is int b and > 0 ? b : 32;

    /// <summary>Nearest even integer ≥ 2.</summary>
    internal static int Even(double x) => Math.Max(2, (int)Math.Round(x / 2.0) * 2);
}
