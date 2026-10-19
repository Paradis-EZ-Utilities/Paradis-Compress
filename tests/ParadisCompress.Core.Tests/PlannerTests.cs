using ParadisCompress.Core.Media;
using ParadisCompress.Core.Planning;
using ParadisCompress.Core.Util;

namespace ParadisCompress.Core.Tests;

public class PlannerTests
{
    internal static MediaInfo Info(int w = 1920, int h = 1080, double fps = 30, double secs = 600, int rotation = 0,
        int channels = 2, bool audio = true, bool video = true, int sampleRate = 48000, long size = 500 * Units.BytesPerMB,
        double? baseFps = null, int sarNum = 1, int sarDen = 1, int audioTracks = 1) => new()
    {
        Path = "/videos/in.mp4",
        FileSizeBytes = size,
        FormatName = "mov,mp4,m4a,3gp,3g2,mj2",
        Duration = TimeSpan.FromSeconds(secs),
        Video = video ? new VideoStreamInfo
        {
            Index = 0, Codec = "h264", CodedWidth = w, CodedHeight = h, Rotation = rotation,
            AverageFps = fps, BaseFps = baseFps ?? fps, SarNum = sarNum, SarDen = sarDen,
        } : null,
        Audio = audio ? new AudioStreamInfo { Index = video ? 1 : 0, Codec = "aac", Channels = channels, SampleRate = sampleRate } : null,
        AudioStreamCount = audio ? audioTracks : 0,
    };

    private static EncodePlan Plan(MediaInfo i, CompressionOptions? o = null) => EncodePlanner.Plan(i, o ?? new CompressionOptions());

    [Fact]
    public void ClassRecordingKeepsResolutionAndUsesCrf()
    {
        var p = Plan(Info(2560, 1440));
        Assert.Equal((2560, 1440), (p.Width, p.Height));
        Assert.False(p.Scaled);
        Assert.Equal(23, p.Crf);
        Assert.False(p.TwoPass);
        Assert.Null(p.OutputFps);
        Assert.Equal(96, p.AudioKbps);
        Assert.Equal(2, p.AudioChannels);
    }

    [Theory]
    [InlineData(60, 30.0)]
    [InlineData(59.94, 30.0)]
    [InlineData(120, 30.0)]
    [InlineData(30, null)]
    [InlineData(29.97, null)]
    [InlineData(25, null)]
    [InlineData(15, null)]
    [InlineData(31, null)] // within 5 %: not worth resampling
    public void FrameRateIsOnlyEverLowered(double src, double? expected)
    {
        Assert.Equal(expected, Plan(Info(fps: src)).OutputFps);
    }

    [Fact]
    public void SmallestFileLowersToFifteenAnd720()
    {
        var p = Plan(Info(1920, 1080, 30), new CompressionOptions { Preset = PresetId.SmallestFile });
        Assert.Equal((1280, 720), (p.Width, p.Height));
        Assert.Equal(15, p.OutputFps);
        Assert.Equal(30, p.Crf);
        Assert.Equal("slow", p.Preset.X264Preset);
        Assert.Equal(64, p.AudioKbps);
    }

    [Fact]
    public void ResolutionIsNeverEnlarged()
    {
        var p = Plan(Info(854, 480), new CompressionOptions { Preset = PresetId.SmallestFile });
        Assert.Equal((854, 480), (p.Width, p.Height));
        Assert.False(p.Scaled);
    }

    [Fact]
    public void LimitAppliesToShorterSideForPortrait()
    {
        // A phone video stored 1920x1080 with a 90° rotation is displayed 1080x1920.
        var p = Plan(Info(1920, 1080, rotation: 90), new CompressionOptions { Preset = PresetId.SmallestFile });
        Assert.Equal((720, 1280), (p.Width, p.Height));
        Assert.Contains(p.Notes, n => n.Contains("upright"));
    }

    [Fact]
    public void RotationWithoutScalingSwapsSize()
    {
        var p = Plan(Info(1280, 720, rotation: 270));
        Assert.Equal((720, 1280), (p.Width, p.Height));
        Assert.False(p.Scaled);
    }

    [Fact]
    public void OddSizeIsPaddedNotCropped()
    {
        var p = Plan(Info(1365, 767));
        Assert.Equal((1366, 768), (p.Width, p.Height));
        Assert.True(p.PaddedToEven);
        Assert.False(p.Scaled);
        Assert.Equal("pad=1366:768:0:0:color=black,setsar=1", FfmpegArguments.VideoFilters(p));
    }

    [Fact]
    public void ScaledSizeIsEvenAndKeepsAspect()
    {
        var p = Plan(Info(2561, 1441), new CompressionOptions { MaxShortSide = 720 });
        Assert.Equal(720, p.Height);
        Assert.Equal(0, p.Width % 2);
        Assert.InRange(p.Width / (double)p.Height, 2561 / 1441.0 - 0.01, 2561 / 1441.0 + 0.01);
    }

    [Fact]
    public void NonSquarePixelsAreMadeSquare()
    {
        var p = Plan(Info(720, 576, sarNum: 16, sarDen: 11));
        Assert.True(p.Scaled);
        Assert.Equal((1048, 576), (p.Width, p.Height));
        Assert.EndsWith("setsar=1", FfmpegArguments.VideoFilters(p));
    }

    [Fact]
    public void AdvancedOverridesWin()
    {
        var p = Plan(Info(1920, 1080, 60), new CompressionOptions { MaxShortSide = 0, MaxFps = 0, Crf = 18, Preset = PresetId.SmallestFile });
        Assert.Equal((1920, 1080), (p.Width, p.Height));
        Assert.Null(p.OutputFps);
        Assert.Equal(18, p.Crf);
    }

    [Fact]
    public void KeyframeIntervalFollowsOutputFrameRate()
    {
        Assert.Equal(300, Plan(Info(fps: 60)).KeyframeInterval);       // 30 fps × 10 s
        Assert.Equal(150, Plan(Info(fps: 30), new CompressionOptions { Preset = PresetId.Balanced }).KeyframeInterval);
    }

    [Fact]
    public void MonoStaysMonoAndStereoStaysStereo()
    {
        Assert.Equal(1, Plan(Info(channels: 1)).AudioChannels);
        Assert.Equal(64, Plan(Info(channels: 1)).AudioKbps);
        Assert.Equal(2, Plan(Info(channels: 2)).AudioChannels);
        Assert.Equal(2, Plan(Info(channels: 6)).AudioChannels);
        Assert.Equal(1, Plan(Info(channels: 2), new CompressionOptions { MixToMono = true }).AudioChannels);
    }

    [Fact]
    public void HighSampleRatesAreReduced()
    {
        Assert.Equal(48000, Plan(Info(sampleRate: 96000)).AudioSampleRate);
        Assert.Null(Plan(Info(sampleRate: 44100)).AudioSampleRate);
    }

    [Fact]
    public void NoAudioMeansNoAudioSettings()
    {
        var p = Plan(Info(audio: false));
        Assert.False(p.IncludeAudio);
        Assert.Contains("-an", FfmpegArguments.Build(p, Info(audio: false), "/a.mp4", "/b.mp4"));
    }

    [Fact]
    public void MultipleAudioTracksAreMentioned()
    {
        Assert.Contains(Plan(Info(audioTracks: 2)).Notes, n => n.Contains("first is kept"));
    }

    [Fact]
    public void VideoModeOnAudioFileExplains()
    {
        var ex = Assert.Throws<CompressionException>(() => Plan(Info(video: false)));
        Assert.Contains("Extract audio", ex.Message);
    }

    [Fact]
    public void AudioOnlyOnSilentVideoExplains()
    {
        var ex = Assert.Throws<CompressionException>(() => Plan(Info(audio: false), new CompressionOptions { Mode = OutputMode.AudioOnly }));
        Assert.Equal(FailureKind.InvalidInput, ex.Kind);
    }

    // ---------------------------------------------------------------- target size

    [Fact]
    public void TargetBudgetArithmetic()
    {
        // 10 minutes into 100 MB at 1080p30 with stereo audio.
        var p = Plan(Info(secs: 600), new CompressionOptions { TargetSizeMB = 100 });
        Assert.True(p.TwoPass);
        long targetBytes = 100 * Units.BytesPerMB;
        double budget = targetBytes * 8 * EncodePlanner.TargetHeadroom;
        double overhead = (4096 + 20.0 * 30 * 600 + 6.0 * 48000 / 1024 * 600) * 8;
        double expectedVideo = (budget - overhead) / 600 - 96_000;
        Assert.Equal((long)expectedVideo, p.VideoBitsPerSecond);
        Assert.Equal(TargetVerdict.Ok, p.Target!.Verdict);
        // Sanity: about 1.18 Mbit/s of video (100 MB = 100,000,000 bytes).
        Assert.InRange(p.VideoBitsPerSecond!.Value, 1_150_000, 1_200_000);
        // Video + audio + overhead never exceeds the target.
        double total = (p.VideoBitsPerSecond.Value + p.AudioKbps * 1000.0) * 600 / 8 + overhead / 8;
        Assert.True(total < targetBytes);
    }

    [Fact]
    public void FiveHundredMegabyteTargetIsFiveHundredMillionBytes()
    {
        // The rc.1 report: a "500 MB" target produced 503,362,712 bytes (under 500 MiB, over 500 MB).
        var p = Plan(Info(secs: 3600, size: 2_000_000_000), new CompressionOptions { TargetSizeMB = 500 });
        Assert.Equal(500_000_000, p.Target!.TargetBytes);
        Assert.Equal(480_000_000, p.Target.AimBytes);
        double overhead = 4096 + 20.0 * 30 * 3600 + 6.0 * 48000 / 1024 * 3600;
        double planned = (p.VideoBitsPerSecond!.Value + p.AudioKbps * 1000.0) * 3600 / 8 + overhead;
        Assert.True(planned <= 480_000_000, $"planned {planned:N0} bytes");
        Assert.True(503_362_712 > p.Target.TargetBytes);
        Assert.Contains("Aims for about 480 MB", p.Target.ShortMessage);
    }

    [Fact]
    public void ResultAboveTargetIsDetectedInExactBytes()
    {
        var info = Info(secs: 3600, size: 2_000_000_000);
        var p = Plan(info, new CompressionOptions { TargetSizeMB = 500 });
        var over = new ParadisCompress.Core.Jobs.JobResult("/o.mp4", info.FileSizeBytes, 503_362_712, info, p, TimeSpan.FromMinutes(5));
        var exact = over with { OutputBytes = 500_000_000 };
        Assert.True(over.AboveTarget);
        Assert.False(exact.AboveTarget);

        var opts = new CompressionOptions { TargetSizeMB = 500 };
        var retry = over.RetryOptions(opts);
        Assert.NotNull(retry);
        Assert.Equal(500, retry!.TargetSizeMB);
        Assert.True(retry.VideoBitsPerSecondCap < p.VideoBitsPerSecond);
        Assert.Null(exact.RetryOptions(opts));

        // The retry plan keeps the same target bytes and uses the lower bitrate.
        var p2 = Plan(info, retry);
        Assert.Equal(500_000_000, p2.Target!.TargetBytes);
        Assert.Equal(retry.VideoBitsPerSecondCap, p2.VideoBitsPerSecond);
        Assert.Contains(p2.Notes, n => n.Contains("lower video bitrate"));
    }

    [Fact]
    public void BitrateCapNeverRaisesTheBitrate()
    {
        var p = Plan(Info(secs: 600), new CompressionOptions { TargetSizeMB = 100, VideoBitsPerSecondCap = 50_000_000 });
        var q = Plan(Info(secs: 600), new CompressionOptions { TargetSizeMB = 100 });
        Assert.Equal(q.VideoBitsPerSecond, p.VideoBitsPerSecond);
        var crf = Plan(Info(secs: 600), new CompressionOptions { VideoBitsPerSecondCap = 500_000 });
        Assert.False(crf.TwoPass);
    }

    [Fact]
    public void AudioRetryStepsDownOneMp3Bitrate()
    {
        var info = Info(secs: 600);
        var p = Plan(info, new CompressionOptions { Mode = OutputMode.AudioOnly, TargetSizeMB = 10 });
        var over = new ParadisCompress.Core.Jobs.JobResult("/o.mp3", info.FileSizeBytes, 10_000_001, info, p, TimeSpan.FromSeconds(9));
        var retry = over.RetryOptions(new CompressionOptions { Mode = OutputMode.AudioOnly, TargetSizeMB = 10 });
        Assert.Equal(EncodePlanner.NextLowerMp3(p.AudioKbps), retry!.Mp3Kbps);
        Assert.Null(EncodePlanner.NextLowerMp3(32));
    }

    [Fact]
    public void EveryTargetHasAShortMessage()
    {
        foreach (var mb in new[] { 1.0, 20, 40, 200, 2000 })
        foreach (var mode in new[] { OutputMode.Video, OutputMode.AudioOnly })
        {
            var p = Plan(Info(secs: 3600), new CompressionOptions { Mode = mode, TargetSizeMB = mb });
            Assert.False(string.IsNullOrWhiteSpace(p.Target!.ShortMessage));
            Assert.True(p.Target.ShortMessage.Length <= 80, p.Target.ShortMessage);
        }
    }

    [Fact]
    public void AudioTargetSaysWhatItDoes()
    {
        var p = Plan(Info(secs: 3600), new CompressionOptions { Mode = OutputMode.AudioOnly, TargetSizeMB = 100 });
        Assert.Contains("96 kbit/s", p.Target!.ShortMessage);
        Assert.Contains("MP3", p.Target.ShortMessage);
    }

    [Fact]
    public void TinyTargetIsImpossible()
    {
        var p = Plan(Info(secs: 3 * 3600), new CompressionOptions { TargetSizeMB = 20 });
        Assert.Equal(TargetVerdict.Impossible, p.Target!.Verdict);
        Assert.Contains("Extract audio", p.Target.Message);
    }

    [Fact]
    public void VeryLowTargetWarnsWithSuggestions()
    {
        // 2 hours into 200 MB at 1080p30: ~120 kbit/s of video after audio.
        var p = Plan(Info(secs: 7200), new CompressionOptions { TargetSizeMB = 200 });
        Assert.Equal(TargetVerdict.TooLow, p.Target!.Verdict);
        Assert.Contains("lower resolution", p.Target.Message);
        Assert.Contains("Extract audio", p.Target.Message);
        Assert.True(p.Target.SuggestedMinimumMB > 200);
    }

    [Fact]
    public void LowerResolutionImprovesTheVerdict()
    {
        var full = Plan(Info(secs: 3600), new CompressionOptions { TargetSizeMB = 150 });
        var small = Plan(Info(secs: 3600), new CompressionOptions { TargetSizeMB = 150, MaxShortSide = 720 });
        Assert.True(small.Target!.BitsPerPixel > full.Target!.BitsPerPixel * 2);
    }

    [Fact]
    public void AudioShrinksWhenBudgetIsTight()
    {
        var p = Plan(Info(secs: 3600), new CompressionOptions { TargetSizeMB = 40 });
        Assert.InRange(p.AudioKbps, 48, 95);
    }

    [Fact]
    public void TargetBiggerThanSourceIsPointedOut()
    {
        var p = Plan(Info(secs: 60, size: 10 * Units.BytesPerMB), new CompressionOptions { TargetSizeMB = 50 });
        Assert.Contains("not smaller than the original", p.Target!.Message);
    }

    [Fact]
    public void TwoPassArgumentsDifferPerPass()
    {
        var info = Info(fps: 60);
        var p = Plan(info, new CompressionOptions { TargetSizeMB = 50 });
        var a1 = FfmpegArguments.Build(p, info, "/in.mp4", "/out.mp4", 1, "/tmp/x/pass");
        var a2 = FfmpegArguments.Build(p, info, "/in.mp4", "/out.mp4", 2, "/tmp/x/pass");
        Assert.Contains("-an", a1);
        Assert.Equal(["-f", "null", "-"], a1[^3..]);
        Assert.DoesNotContain("file:/out.mp4", a1);
        Assert.Contains("aac", a2);
        Assert.Equal("file:/out.mp4", a2[^1]);
        Assert.Equal("1", a1[a1.IndexOf("-pass") + 1]);
        Assert.Equal("2", a2[a2.IndexOf("-pass") + 1]);
        Assert.Equal("/tmp/x/pass", a2[a2.IndexOf("-passlogfile") + 1]);
        Assert.StartsWith("fps=30", a1[a1.IndexOf("-vf") + 1]);
    }

    [Fact]
    public void ArgumentsAreAListAndPathsAreLiteralFileUrls()
    {
        var info = Info();
        string weird = "/v/Lecture \"1\" & 'notes' %d; $(rm -rf) 日本.mp4";
        var a = FfmpegArguments.Build(Plan(info), info, weird, "/out/x.mp4");
        Assert.Contains("file:" + weird, a);
        Assert.Contains("-n", a);              // never overwrite anything
        Assert.Contains("-nostdin", a);
        Assert.Equal("0:0", a[a.IndexOf("-map") + 1]);
        Assert.Contains("+faststart", a);
    }

    // ---------------------------------------------------------------- audio only

    [Fact]
    public void AudioOnlyDefaults()
    {
        var p = Plan(Info(), new CompressionOptions { Mode = OutputMode.AudioOnly });
        Assert.Equal(".mp3", p.Extension);
        Assert.Equal(96, p.AudioKbps);
        Assert.Equal(2, p.AudioChannels);
        var mono = Plan(Info(channels: 1), new CompressionOptions { Mode = OutputMode.AudioOnly });
        Assert.Equal(64, mono.AudioKbps);
        Assert.Equal(1, mono.AudioChannels);
        var a = FfmpegArguments.Build(p, Info(), "/in.mp4", "/out.mp3");
        Assert.Contains("libmp3lame", a);
        Assert.Contains("-vn", a);
        Assert.Equal("0:1", a[a.IndexOf("-map") + 1]);
    }

    [Theory]
    [InlineData(3600, 30, 64)]   // 1 h into 30 MB → ~68 kbit/s → 64
    [InlineData(3600, 100, 96)]  // plenty of room: stays at the chosen 96
    [InlineData(3600, 20, 40)]
    public void AudioOnlyTargetPicksALameBitrateThatFits(double secs, double mb, int expected)
    {
        var p = Plan(Info(secs: secs), new CompressionOptions { Mode = OutputMode.AudioOnly, TargetSizeMB = mb });
        Assert.Equal(expected, p.AudioKbps);
        Assert.True(p.AudioKbps * 1000.0 / 8 * secs < mb * Units.BytesPerMB);
    }

    [Fact]
    public void AudioOnlyTargetTooSmall()
    {
        var p = Plan(Info(secs: 3 * 3600), new CompressionOptions { Mode = OutputMode.AudioOnly, TargetSizeMB = 10 });
        Assert.Equal(TargetVerdict.Impossible, p.Target!.Verdict);
    }

    [Fact]
    public void ZeroDurationIsRejected()
    {
        var i = Info() with { Duration = null };
        Assert.Throws<CompressionException>(() => Plan(i));
    }
}
