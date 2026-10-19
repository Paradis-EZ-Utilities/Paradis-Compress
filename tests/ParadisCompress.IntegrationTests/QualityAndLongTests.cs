using System.Globalization;
using ParadisCompress.Core.Planning;
using Xunit.Abstractions;

namespace ParadisCompress.IntegrationTests;

/// <summary>
/// Produces the material for the visual readability check: matching source/output frames at several
/// timestamps, at native resolution, plus 1:1 crops of the smallest text. SSIM/PSNR are recorded as a
/// supplement; the frames are what a person (or Claude) actually looks at. Assertions here are loose
/// floors that catch a broken encode, not a judgement of readability.
/// </summary>
public class QualityTests(ITestOutputHelper output)
{
    public static TheoryData<string, string, double?> Cases => new()
    {
        { "class_code_1080p30.mp4", "ClassRecording", null },
        { "class_code_1080p30.mp4", "Balanced", null },
        { "class_code_1080p30.mp4", "SmallestFile", null },
        { "class_code_1080p30.mp4", "ClassRecording", 2.0 },   // a tight 2 MB target for 40 s of 1080p
        { "slides_720p25.mp4", "ClassRecording", null },
        { "slides_720p25.mp4", "SmallestFile", null },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task FramesForVisualInspection(string fixture, string preset, double? targetMB)
    {
        string src = Fixtures.Get(fixture);
        var srcInfo = await T.Probe(src);
        string dir = T.NewOutDir("quality");
        var opts = new CompressionOptions { Preset = Enum.Parse<PresetId>(preset), TargetSizeMB = targetMB };
        var run = await T.Compress(src, opts, dir);
        var r = run.Result;
        int sw = srcInfo.Video!.DisplayWidth, sh = srcInfo.Video.DisplayHeight;
        var (ssim, psnr) = T.Quality(r.OutputPath, src, sw, sh);

        string label = $"{Path.GetFileNameWithoutExtension(fixture)}-{preset}" + (targetMB is double t ? $"-target{t.ToString(CultureInfo.InvariantCulture)}MB" : "");
        string frames = Path.Combine(T.Artifacts, "frames", label);
        double scale = r.Plan.Width / (double)sw;
        foreach (double ts in new[] { 5.0, 15.0, 25.0, 35.0 })
        {
            string tsName = $"{ts:00}s";
            T.SaveFrame(src, ts, Path.Combine(frames, $"{tsName}-source.png"));
            T.SaveFrame(r.OutputPath, ts, Path.Combine(frames, $"{tsName}-output.png"));
            if (fixture.StartsWith("class_code"))
            {
                // 1:1 crops of the 18 px code (top left) and the 13 px terminal (bottom right), at each file's own resolution.
                T.SaveFrame(src, ts, Path.Combine(frames, $"{tsName}-code-source.png"), "crop=760:300:30:60");
                T.SaveFrame(r.OutputPath, ts, Path.Combine(frames, $"{tsName}-code-output.png"), Crop(760, 300, 30, 60, scale));
                T.SaveFrame(src, ts, Path.Combine(frames, $"{tsName}-terminal-source.png"), "crop=700:220:1180:600");
                T.SaveFrame(r.OutputPath, ts, Path.Combine(frames, $"{tsName}-terminal-output.png"), Crop(700, 220, 1180, 600, scale));
            }
            else
            {
                T.SaveFrame(src, ts, Path.Combine(frames, $"{tsName}-body-source.png"), "crop=900:300:60:130");
                T.SaveFrame(r.OutputPath, ts, Path.Combine(frames, $"{tsName}-body-output.png"), Crop(900, 300, 60, 130, scale));
            }
        }

        Assert.True(ssim > (preset == "SmallestFile" || targetMB != null ? 0.80 : 0.93), $"SSIM {ssim}");
        output.WriteLine($"{label}: {r.SourceBytes:N0} -> {r.OutputBytes:N0} bytes, SSIM {ssim:0.0000}, PSNR {psnr:0.00} dB, {run.Wall.TotalSeconds:0.0} s; {r.Plan.Describe()}");
        T.Record(nameof(FramesForVisualInspection), new
        {
            label, fixture, preset, targetMB, srcBytes = r.SourceBytes, outBytes = r.OutputBytes, reductionPct = r.Reduction * 100,
            ssim, psnr, seconds = run.Wall.TotalSeconds, srcDur = srcInfo.Duration!.Value.TotalSeconds, plan = r.Plan.Describe(),
        });
        Directory.Delete(dir, true);
    }

    private static string Crop(int w, int h, int x, int y, double s) =>
        string.Create(CultureInfo.InvariantCulture, $"crop={(int)(w * s)}:{(int)(h * s)}:{(int)(x * s)}:{(int)(y * s)}");
}

/// <summary>
/// A one-hour synthetic recording (360p, 15 fps, a sync marker every 2 s). Slow, so it is tagged
/// Long; run with --filter Category=Long. Checks duration and A/V sync at the start, middle and end.
/// </summary>
public class LongRecordingTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Long")]
    public async Task OneHourRecordingStaysInSync()
    {
        string src = Fixtures.Get("long_60min_360p15.mp4");
        var srcInfo = await T.Probe(src);
        string dir = T.NewOutDir("long");
        var run = await T.Compress(src, new CompressionOptions(), dir);
        var o = await T.Probe(run.Result.OutputPath);
        double dd = o.Duration!.Value.TotalSeconds - srcInfo.Duration!.Value.TotalSeconds;
        Assert.InRange(dd, -0.2, 0.2);
        var results = new Dictionary<string, double[]>();
        foreach (var (name, from) in new[] { ("start", 0.0), ("middle", 1800.0), ("end", 3570.0) })
        {
            var offs = T.SyncOffsets(run.Result.OutputPath, from, 28);
            Assert.True(offs.Count >= 10, $"{name}: {offs.Count} markers");
            foreach (double off in offs) Assert.InRange(off, -0.08, 0.08);
            results[name] = offs.Select(x => Math.Round(x * 1000, 1)).ToArray();
        }
        output.WriteLine($"1 h: {run.Result.SourceBytes:N0} -> {run.Result.OutputBytes:N0} in {run.Wall.TotalSeconds:0} s, duration diff {dd * 1000:0} ms");
        T.Record(nameof(OneHourRecordingStaysInSync), new { run.Result.SourceBytes, run.Result.OutputBytes, seconds = run.Wall.TotalSeconds, durationDiffMs = dd * 1000, syncMs = results });
        Directory.Delete(dir, true);
    }
}
