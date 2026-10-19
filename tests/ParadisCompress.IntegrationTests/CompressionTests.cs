using ParadisCompress.Core.Planning;
using Xunit.Abstractions;

namespace ParadisCompress.IntegrationTests;

/// <summary>
/// Every fixture through the default Class Recording preset, checking the things that "it encoded"
/// doesn't prove: the file decodes completely, has the right streams, size, frame rate and length,
/// stays in sync, and the source is byte-for-byte unchanged.
/// </summary>
public class CompressionTests(ITestOutputHelper output)
{

    public static TheoryData<string, int, int, double, int, bool> Cases => new()
    {
        // fixture,                     width, height, fps (0 = keep source timing), audio channels, has sync markers
        { "class_code_1080p30.mp4",      1920, 1080, 30, 2, false },
        { "slides_720p25.mp4",           1280,  720, 25, 2, false },
        { "motion_720p30.mp4",           1280,  720, 30, 2, false },
        { "sync_640x360_30.mp4",          640,  360, 30, 2, true },
        { "hfr_1280x720_60.mp4",         1280,  720, 30, 2, true },
        { "noaudio_720p30.mp4",          1280,  720, 30, 0, false },
        { "odd_641x361.mkv",              642,  362, 30, 1, false },
        { "rotated_90.mp4",               720, 1280, 30, 1, false },
        { "vfr_low_sync.mp4",            1280,  720,  0, 2, true },
        { "vfr_high_sync.mp4",           1280,  720, 30, 2, true },
        { "mono_speech_640x360.mp4",      640,  360, 25, 1, false },
        { "surround_51.mkv",              320,  240, 30, 2, false },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ClassRecordingPresetProducesAUsableFile(string fixture, int w, int h, double fps, int channels, bool sync)
    {
        string src = Fixtures.Get(fixture);
        string hashBefore = T.Sha256(src);
        var srcInfo = await T.Probe(src);
        string dir = T.NewOutDir("class-" + Path.GetFileNameWithoutExtension(fixture));

        var run = await T.Compress(src, new CompressionOptions(), dir);
        var r = run.Result;

        // The source is untouched and nothing but the result is left in the folder.
        Assert.Equal(hashBefore, T.Sha256(src));
        Assert.Equal([Path.GetFileName(r.OutputPath)], Directory.GetFiles(dir).Select(Path.GetFileName));

        // Reported size is the real size.
        Assert.Equal(new FileInfo(r.OutputPath).Length, r.OutputBytes);
        Assert.Equal(new FileInfo(src).Length, r.SourceBytes);

        // Streams, size, frame rate.
        var o = await T.Probe(r.OutputPath);
        Assert.Equal("h264", o.Video!.Codec);
        Assert.Equal("yuv420p", o.Video.PixelFormat);
        Assert.Equal(0, o.Video.Rotation);
        Assert.Equal((w, h), (o.Video.DisplayWidth, o.Video.DisplayHeight));
        if (channels == 0) Assert.Null(o.Audio);
        else
        {
            Assert.Equal("aac", o.Audio!.Codec);
            Assert.Equal(channels, o.Audio.Channels);
        }
        double expectedFps = fps > 0 ? fps : srcInfo.Video!.AverageFps!.Value;
        Assert.InRange(o.Video.AverageFps!.Value, expectedFps * 0.97, expectedFps * 1.03);

        // Length.
        double dSrc = srcInfo.Duration!.Value.TotalSeconds, dOut = o.Duration!.Value.TotalSeconds;
        Assert.InRange(dOut - dSrc, -0.15, 0.15);
        if (o.Video != null && srcInfo.Video != null)
        {
            var vt = T.FrameTimes(r.OutputPath);
            Assert.InRange(vt[^1] - vt[0], dSrc - 0.25, dSrc + 0.1);
        }

        // Decodes start to finish without a single error.
        Assert.Equal("", T.FullDecodeErrors(r.OutputPath));

        // Sync.
        List<double>? srcOffsets = null, outOffsets = null;
        if (sync)
        {
            srcOffsets = T.SyncOffsets(src);
            outOffsets = T.SyncOffsets(r.OutputPath);
            Assert.True(outOffsets.Count >= srcOffsets.Count - 1 && outOffsets.Count >= 5, $"markers found: src {srcOffsets.Count}, out {outOffsets.Count}");
            double frame = 1.0 / o.Video.AverageFps!.Value;
            foreach (double off in outOffsets) Assert.InRange(off, -frame - 0.012, frame + 0.012);
            Assert.InRange(outOffsets.Average() - srcOffsets.Average(), -0.02, 0.02);
        }

        output.WriteLine($"{fixture}: {r.SourceBytes:N0} -> {r.OutputBytes:N0} bytes ({-r.Reduction * 100:+0.0;-0.0}%), {run.Wall.TotalSeconds:0.0}s, " +
                         $"dur {dSrc:0.000}->{dOut:0.000}, fps {o.Video.AverageFps:0.###}, {r.Plan.Describe()}");
        T.Record(nameof(ClassRecordingPresetProducesAUsableFile), new
        {
            fixture, srcBytes = r.SourceBytes, outBytes = r.OutputBytes, reductionPct = r.Reduction * 100, didNotShrink = r.DidNotShrink,
            seconds = run.Wall.TotalSeconds, srcDur = dSrc, outDur = dOut, outFps = o.Video.AverageFps, w = o.Video.DisplayWidth, h = o.Video.DisplayHeight,
            outChannels = o.Audio?.Channels, srcSyncMs = srcOffsets?.Select(x => Math.Round(x * 1000, 1)), outSyncMs = outOffsets?.Select(x => Math.Round(x * 1000, 1)),
            plan = r.Plan.Describe(),
        });
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task RotatedVideoIsUprightAndMatchesHowPlayersShowTheSource()
    {
        string src = Fixtures.Get("rotated_90.mp4");
        var info = await T.Probe(src);
        Assert.Equal(90, info.Video!.Rotation is 90 or 270 ? 90 : info.Video.Rotation);
        string dir = T.NewOutDir("rotated");
        var run = await T.Compress(src, new CompressionOptions(), dir);
        // ffmpeg applies the display matrix when decoding the source, so this compares like with like.
        var (ssim, psnr) = T.Quality(run.Result.OutputPath, src, info.Video.DisplayWidth, info.Video.DisplayHeight);
        Assert.True(ssim > 0.95, $"SSIM {ssim}");
        T.SaveFrame(run.Result.OutputPath, 2, Path.Combine(T.Artifacts, "frames", "rotated-output-2s.png"));
        T.SaveFrame(src, 2, Path.Combine(T.Artifacts, "frames", "rotated-source-as-displayed-2s.png"));
        T.Record(nameof(RotatedVideoIsUprightAndMatchesHowPlayersShowTheSource), new { ssim, psnr, info.Video.Rotation });
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task OddSizeKeepsEverySourcePixel()
    {
        string src = Fixtures.Get("odd_641x361.mkv");
        string dir = T.NewOutDir("odd");
        var run = await T.Compress(src, new CompressionOptions(), dir);
        // Crop the padding off the output: what's left must match the whole source, red border included.
        var r = T.Exec(T.Tools.FfmpegPath, "-hide_banner", "-nostdin", "-i", "file:" + run.Result.OutputPath, "-i", "file:" + src,
            "-lavfi", "[0:v]format=yuv444p,crop=641:361:0:0[a];[1:v]format=yuv444p[b];[a][b]ssim", "-f", "null", "-");
        double ssim = double.Parse(System.Text.RegularExpressions.Regex.Match(r.Err, @"All:([0-9.]+)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(ssim > 0.95, $"SSIM {ssim}");
        T.SaveFrame(run.Result.OutputPath, 1, Path.Combine(T.Artifacts, "frames", "odd-output-bottom-right-x4.png"), "crop=40:40:602:322,scale=160:160:flags=neighbor");
        T.SaveFrame(src, 1, Path.Combine(T.Artifacts, "frames", "odd-source-bottom-right-x4.png"), "crop=40:40:601:321,scale=160:160:flags=neighbor");
        T.Record(nameof(OddSizeKeepsEverySourcePixel), new { ssim });
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task VariableFrameRateFixturesReallyAreVariable()
    {
        var low = await T.Probe(Fixtures.Get("vfr_low_sync.mp4"));
        var high = await T.Probe(Fixtures.Get("vfr_high_sync.mp4"));
        Assert.True(low.Video!.LooksVariableFrameRate, $"avg {low.Video.AverageFps} base {low.Video.BaseFps}");
        var lowTimes = T.FrameTimes(Fixtures.Get("vfr_low_sync.mp4"));
        var gaps = lowTimes.Zip(lowTimes.Skip(1), (a, b) => Math.Round(b - a, 3)).Distinct().Count();
        Assert.True(gaps >= 2, "frame durations should vary");
        T.Record(nameof(VariableFrameRateFixturesReallyAreVariable), new
        {
            lowAvg = low.Video.AverageFps, lowBase = low.Video.BaseFps, highAvg = high.Video!.AverageFps, highBase = high.Video.BaseFps,
            lowDistinctFrameDurations = gaps,
        });
    }

    [Fact]
    public async Task AlreadyCompressedSourceIsReportedHonestly()
    {
        string src = Fixtures.Get("precompressed_slides_720p25.mp4");
        string dir = T.NewOutDir("precompressed");
        var run = await T.Compress(src, new CompressionOptions(), dir);
        var r = run.Result;
        Assert.Equal(r.OutputBytes >= r.SourceBytes, r.DidNotShrink);
        Assert.True(File.Exists(src));
        Assert.True(File.Exists(r.OutputPath));
        output.WriteLine($"precompressed: {r.SourceBytes:N0} -> {r.OutputBytes:N0} (didNotShrink={r.DidNotShrink})");
        T.Record(nameof(AlreadyCompressedSourceIsReportedHonestly), new { r.SourceBytes, r.OutputBytes, r.DidNotShrink, reductionPct = r.Reduction * 100 });
        Directory.Delete(dir, true);
    }
}
