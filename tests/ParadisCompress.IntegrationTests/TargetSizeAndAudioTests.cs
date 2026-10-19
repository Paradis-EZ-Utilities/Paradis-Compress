using ParadisCompress.Core.Jobs;
using ParadisCompress.Core.Planning;
using ParadisCompress.Core.Util;
using Xunit.Abstractions;

namespace ParadisCompress.IntegrationTests;

public class TargetSizeTests(ITestOutputHelper output)
{
    /// <summary>
    /// Requirement (rc.2): the result must never exceed the target in exact bytes, with targets in decimal
    /// MB (1 MB = 1,000,000 bytes). For content
    /// with enough detail to use the budget it should land within 15 % under it. Static slides may
    /// legitimately come out far smaller (there's nothing to spend the bits on), so only the upper
    /// bound applies to them.
    /// </summary>
    public static TheoryData<string, double, bool, string> Cases => new()
    {
        { "class_code_1080p30.mp4", 5.0, true, "ClassRecording" },
        { "class_code_1080p30.mp4", 2.0, false, "ClassRecording" },
        { "motion_720p30.mp4", 4.0, true, "ClassRecording" },
        { "motion_720p30.mp4", 4.0, true, "SmallestFile" },
        { "hfr_1280x720_60.mp4", 3.0, true, "Balanced" },
        { "slides_720p25.mp4", 1.5, false, "ClassRecording" },
        { "noaudio_720p30.mp4", 1.0, true, "ClassRecording" },
        // rc.2: fractional and "upload limit" style decimal targets.
        { "class_code_1080p30.mp4", 3.5, true, "ClassRecording" },
        { "motion_720p30.mp4", 2.5, true, "Balanced" },
        { "slides_720p25.mp4", 0.75, false, "SmallestFile" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ResultLandsWithinToleranceOfTarget(string fixture, double targetMB, bool expectNearTarget, string preset)
    {
        string src = Fixtures.Get(fixture);
        string dir = T.NewOutDir("target");
        var tempRoot = Path.Combine(Path.GetTempPath(), "ParadisCompress");
        var before = Directory.Exists(tempRoot) ? Directory.GetDirectories(tempRoot).ToHashSet() : [];

        var run = await T.Compress(src, new CompressionOptions { TargetSizeMB = targetMB, Preset = Enum.Parse<PresetId>(preset) }, dir);
        var r = run.Result;
        long target = Units.MBToBytes(targetMB);
        double ratio = r.OutputBytes / (double)target;
        output.WriteLine($"{fixture} {preset} target {targetMB} MB: {r.OutputBytes:N0} bytes = {ratio:P1} of target; {r.Plan.Describe()}");

        Assert.Equal((long)Math.Round(targetMB * 1_000_000), target);
        Assert.True(r.OutputBytes <= target, $"over target: {r.OutputBytes:N0} > {target:N0} bytes ({ratio:P2})");
        Assert.Equal(r.OutputBytes, new FileInfo(r.OutputPath).Length);
        if (expectNearTarget) Assert.True(ratio >= 0.85, $"far under target: {ratio:P2}");
        Assert.True(r.Plan.TwoPass);
        int corrections = run.Progress.Max(p => p.PassCount) - 2;

        // Both passes reported progress, in order, and it never went backwards.
        Assert.Contains(run.Progress, p => p.Stage == JobStage.Analysing);
        Assert.Contains(run.Progress, p => p.Stage == JobStage.Encoding && p.Pass == 2);
        int firstEncode = run.Progress.FindIndex(p => p.Stage == JobStage.Encoding);
        Assert.DoesNotContain(run.Progress.Skip(firstEncode), p => p.Stage == JobStage.Analysing);
        // Only a corrective re-run (which adds a pass) may step the bar back.
        for (int i = 1; i < run.Progress.Count; i++)
            if (run.Progress[i].PassCount == run.Progress[i - 1].PassCount)
                Assert.True(run.Progress[i].Fraction >= run.Progress[i - 1].Fraction - 1e-9);
        Assert.False(r.AboveTarget);

        // Two-pass logs and temporary files are gone.
        var after = Directory.Exists(tempRoot) ? Directory.GetDirectories(tempRoot).ToHashSet() : [];
        Assert.Empty(after.Except(before));
        Assert.Single(Directory.GetFiles(dir));
        Assert.Equal("", T.FullDecodeErrors(r.OutputPath));

        // How long each pass took, to check the progress weighting.
        var pass1End = run.Progress.LastOrDefault(p => p.Stage == JobStage.Analysing)?.Elapsed;
        var total = run.Progress.Last().Elapsed;
        T.Record(nameof(ResultLandsWithinToleranceOfTarget), new
        {
            fixture, preset, targetMB, targetBytes = target, outBytes = r.OutputBytes, ratio, videoKbps = r.Plan.VideoBitsPerSecond / 1000,
            corrections, audioKbps = r.Plan.AudioKbps, verdict = r.Plan.Target?.Verdict.ToString(), bpp = r.Plan.Target?.BitsPerPixel,
            pass1Share = pass1End is TimeSpan p1 ? p1.TotalSeconds / total.TotalSeconds : (double?)null, seconds = run.Wall.TotalSeconds,
        });
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task ImpossibleTargetIsRefusedBeforeEncoding()
    {
        string src = Fixtures.Get("class_code_1080p30.mp4");
        string dir = T.NewOutDir("target-impossible");
        var ex = await Assert.ThrowsAsync<CompressionException>(() => T.Compress(src, new CompressionOptions { TargetSizeMB = 0.1 }, dir));
        Assert.Equal(FailureKind.TargetTooSmall, ex.Kind);
        Assert.Empty(Directory.GetFiles(dir));
        Directory.Delete(dir, true);
    }
}

public class AudioOnlyTests(ITestOutputHelper output)
{
    public static TheoryData<string, bool, int, int> Cases => new()
    {
        // fixture, mix to mono, expected channels, expected kbit/s
        { "class_code_1080p30.mp4", false, 2, 96 },
        { "class_code_1080p30.mp4", true, 1, 64 },
        { "mono_speech_640x360.mp4", false, 1, 64 },
        { "surround_51.mkv", false, 2, 96 },
        { "odd_641x361.mkv", false, 1, 64 },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ExtractsSpeechFriendlyMp3(string fixture, bool mono, int channels, int kbps)
    {
        string src = Fixtures.Get(fixture);
        var srcInfo = await T.Probe(src);
        string dir = T.NewOutDir("audio");
        var run = await T.Compress(src, new CompressionOptions { Mode = OutputMode.AudioOnly, MixToMono = mono }, dir);
        var o = await T.Probe(run.Result.OutputPath);
        Assert.EndsWith(".mp3", run.Result.OutputPath);
        Assert.Null(o.Video);
        Assert.Equal("mp3", o.Audio!.Codec);
        Assert.Equal(channels, o.Audio.Channels);
        Assert.InRange(o.Duration!.Value.TotalSeconds - srcInfo.Duration!.Value.TotalSeconds, -0.1, 0.1);
        double actualKbps = run.Result.OutputBytes * 8 / o.Duration.Value.TotalSeconds / 1000;
        Assert.InRange(actualKbps, kbps * 0.95, kbps * 1.1);
        Assert.Equal("", T.FullDecodeErrors(run.Result.OutputPath));
        output.WriteLine($"{fixture} mono={mono}: {run.Result.OutputBytes:N0} bytes, {actualKbps:0.0} kbit/s, {o.Audio.Channels} ch, {o.Audio.SampleRate} Hz");
        T.Record(nameof(ExtractsSpeechFriendlyMp3), new { fixture, mono, run.Result.OutputBytes, actualKbps, o.Audio.Channels, o.Audio.SampleRate, outDur = o.Duration.Value.TotalSeconds, srcDur = srcInfo.Duration.Value.TotalSeconds });
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task AudioOnlyTargetSizeIsRespected()
    {
        string src = Fixtures.Get("class_code_1080p30.mp4");
        string dir = T.NewOutDir("audio-target");
        var run = await T.Compress(src, new CompressionOptions { Mode = OutputMode.AudioOnly, TargetSizeMB = 0.25 }, dir);
        Assert.True(run.Result.OutputBytes <= Units.MBToBytes(0.25));
        T.Record(nameof(AudioOnlyTargetSizeIsRespected), new { run.Result.OutputBytes, kbps = run.Result.Plan.AudioKbps });
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task SilentVideoCannotBeExtracted()
    {
        string dir = T.NewOutDir("audio-none");
        var ex = await Assert.ThrowsAsync<CompressionException>(() =>
            T.Compress(Fixtures.Get("noaudio_720p30.mp4"), new CompressionOptions { Mode = OutputMode.AudioOnly }, dir));
        Assert.Contains("no audio", ex.Message);
        Directory.Delete(dir, true);
    }
}
