using System.Diagnostics;
using ParadisCompress.Core.Jobs;
using ParadisCompress.Core.Media;
using ParadisCompress.Core.Planning;
using ParadisCompress.Core.Util;
using Xunit.Abstractions;

namespace ParadisCompress.IntegrationTests;

public class PathAndInputTests(ITestOutputHelper output)
{
    private const string WeirdFolder = "Ünïcødé 日本語 & 'q' \"dq\" $HOME %TEMP% ;(1)! #x";
    private const string WeirdName = "leçon #1 – été & co's [final] %d %03d (copy) $(echo hi) 'x'";

    [Fact]
    public async Task SpacesUnicodeAndShellCharactersInPathsWork()
    {
        string root = T.NewOutDir("weird");
        string inDir = Path.Combine(root, WeirdFolder, "in put");
        string outDir = Path.Combine(root, WeirdFolder, "out put 出力");
        Directory.CreateDirectory(inDir);
        Directory.CreateDirectory(outDir);
        string src = Path.Combine(inDir, WeirdName + ".mp4");
        File.Copy(Fixtures.Get("sync_640x360_30.mp4"), src);

        var single = await T.Compress(src, new CompressionOptions(), outDir, WeirdName + " compressed");
        Assert.True(File.Exists(single.Result.OutputPath));
        Assert.Equal(WeirdName + " compressed.mp4", Path.GetFileName(single.Result.OutputPath));
        Assert.Equal("", T.FullDecodeErrors(single.Result.OutputPath));

        var audio = await T.Compress(src, new CompressionOptions { Mode = OutputMode.AudioOnly }, outDir, WeirdName);
        Assert.True(File.Exists(audio.Result.OutputPath));

        // Two-pass with the pass logs in a temp folder that itself has Unicode and spaces in its path.
        string oldTmp = Environment.GetEnvironmentVariable("TMPDIR") ?? "";
        string weirdTmp = Path.Combine(root, "tmp 一時 & 'x'");
        Directory.CreateDirectory(weirdTmp);
        Environment.SetEnvironmentVariable("TMPDIR", weirdTmp);
        try
        {
            Assert.StartsWith(weirdTmp, Path.GetTempPath());
            var twoPass = await T.Compress(src, new CompressionOptions { TargetSizeMB = 1 }, outDir, WeirdName + " 2pass");
            Assert.True(File.Exists(twoPass.Result.OutputPath));
            Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(weirdTmp, "ParadisCompress")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TMPDIR", oldTmp.Length == 0 ? null : oldTmp);
        }

        Assert.Equal(3, Directory.GetFiles(outDir).Length);
        T.Record(nameof(SpacesUnicodeAndShellCharactersInPathsWork), new { files = Directory.GetFiles(outDir).Select(Path.GetFileName) });
        Directory.Delete(root, true);
    }

    [Theory]
    [InlineData("notvideo.mp4", FailureKind.InvalidInput)]
    [InlineData("empty.mp4", FailureKind.InvalidInput)]
    [InlineData("truncated.mp4", FailureKind.InvalidInput)]
    [InlineData("picture.png", FailureKind.InvalidInput)]
    public async Task InvalidInputsAreExplained(string fixture, FailureKind kind)
    {
        var ex = await Assert.ThrowsAsync<CompressionException>(() => MediaProber.ProbeAsync(T.Tools, Fixtures.Get(fixture)));
        Assert.Equal(kind, ex.Kind);
        Assert.DoesNotContain("ffprobe", ex.Message);
        Assert.DoesNotContain("moov", ex.Message);
        output.WriteLine($"{fixture}: {ex.Title} — {ex.Message}");
        T.Record(nameof(InvalidInputsAreExplained), new { fixture, ex.Title, ex.Message, ex.Detail });
    }

    [Fact]
    public async Task MissingInputIsExplained()
    {
        var ex = await Assert.ThrowsAsync<CompressionException>(() => MediaProber.ProbeAsync(T.Tools, "/no/such/folder/lecture.mp4"));
        Assert.Equal(FailureKind.InputMissing, ex.Kind);
    }

    [SkippableFact]
    public async Task UnwritableDestinationIsExplainedAndLeavesNothing()
    {
        string? dir = Environment.GetEnvironmentVariable("PARADIS_TEST_READONLY_DIR");
        if (dir == null && !T.IsRoot && !OperatingSystem.IsWindows())
        {
            dir = T.NewOutDir("readonly");
            File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }
        Skip.If(dir == null, "Needs PARADIS_TEST_READONLY_DIR (a read-only mount) when running as root.");
        var ex = await Assert.ThrowsAsync<CompressionException>(() =>
            T.Compress(Fixtures.Get("sync_640x360_30.mp4"), new CompressionOptions(), dir!));
        Assert.Equal(FailureKind.DestinationNotWritable, ex.Kind);
        Assert.Empty(Directory.GetFiles(dir!));
        output.WriteLine($"{ex.Title}: {ex.Message}");
        T.Record(nameof(UnwritableDestinationIsExplainedAndLeavesNothing), new { dir, ex.Title, ex.Message });
    }

    [SkippableFact]
    public async Task FullDiskIsExplainedAndLeavesNothing()
    {
        string? dir = Environment.GetEnvironmentVariable("PARADIS_TEST_SMALL_DISK_DIR");
        Skip.If(dir == null, "Needs PARADIS_TEST_SMALL_DISK_DIR (a mount of a few MB).");
        string src = Fixtures.Get("motion_720p30.mp4");
        var info = await T.Probe(src);
        var pre = CompressionJob.Check(new JobRequest(info, new CompressionOptions(), Path.Combine(dir!, "full.mp4"), false));
        Assert.True(pre.LowDiskSpace, "preflight should warn about low space");
        var ex = await Assert.ThrowsAsync<CompressionException>(() => T.Compress(src, new CompressionOptions(), dir!));
        Assert.Equal(FailureKind.DiskFull, ex.Kind);
        Assert.Empty(Directory.GetFiles(dir!));
        output.WriteLine($"{ex.Title}: {ex.Message}");
        T.Record(nameof(FullDiskIsExplainedAndLeavesNothing), new { free = pre.FreeBytes, estimate = pre.Plan.EstimatedBytes, ex.Title, ex.Message });
    }
}

public class CollisionTests
{
    private static async Task<(string src, MediaInfo info, string dir)> Setup(string name)
    {
        string dir = T.NewOutDir(name);
        string src = Path.Combine(dir, "lecture.mp4");
        File.Copy(Fixtures.Get("sync_640x360_30.mp4"), src);
        return (src, await T.Probe(src), dir);
    }

    [Fact]
    public async Task OutputCanNeverBeTheInput()
    {
        var (src, info, dir) = await Setup("same");
        string hash = T.Sha256(src);
        string sub = Path.Combine(dir, "sub");
        Directory.CreateDirectory(sub);
        var spellings = new List<string>
        {
            src,
            Path.Combine(dir, ".", "lecture.mp4"),
            Path.Combine(sub, "..", "lecture.mp4"),
            Path.Combine(dir, "lecture"),            // extension is added by the job: still the input
        };
        if (!OperatingSystem.IsWindows())
        {
            string link = Path.Combine(dir, "link.mp4");
            File.CreateSymbolicLink(link, src);
            spellings.Add(link);
            string dirLink = Path.Combine(Path.GetTempPath(), "pc-dirlink-" + Guid.NewGuid().ToString("N")[..6]);
            Directory.CreateSymbolicLink(dirLink, dir);
            spellings.Add(Path.Combine(dirLink, "lecture.mp4"));
        }
        foreach (string output in spellings)
        {
            foreach (bool replace in new[] { false, true })
            {
                var ex = await Assert.ThrowsAsync<CompressionException>(() =>
                    CompressionJob.RunAsync(T.Tools, new JobRequest(info, new CompressionOptions(), output, replace), null, default));
                Assert.Equal(FailureKind.OutputIsInput, ex.Kind);
                Assert.Throws<CompressionException>(() => CompressionJob.Check(new JobRequest(info, new CompressionOptions(), output + (output.EndsWith(".mp4") ? "" : ".mp4"), false)));
            }
        }
        Assert.Equal(hash, T.Sha256(src));
        T.Record(nameof(OutputCanNeverBeTheInput), new { spellings });
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task ExistingOutputIsOnlyReplacedWhenConfirmed()
    {
        var (src, info, dir) = await Setup("exists");
        string existing = Path.Combine(dir, "result.mp4");
        File.WriteAllText(existing, "previous result");

        var pre = CompressionJob.Check(new JobRequest(info, new CompressionOptions(), existing, false));
        Assert.True(pre.OutputExists);

        var ex = await Assert.ThrowsAsync<CompressionException>(() =>
            CompressionJob.RunAsync(T.Tools, new JobRequest(info, new CompressionOptions(), existing, false), null, default));
        Assert.Equal(FailureKind.OutputExists, ex.Kind);
        Assert.Equal("previous result", File.ReadAllText(existing));

        string hash = T.Sha256(src);
        var r = await CompressionJob.RunAsync(T.Tools, new JobRequest(info, new CompressionOptions(), existing, true), null, default);
        Assert.Equal(existing, r.OutputPath);
        Assert.True(new FileInfo(existing).Length > 1000);
        Assert.Equal(hash, T.Sha256(src));
        Assert.Equal(["lecture.mp4", "result.mp4"], Directory.GetFiles(dir).Select(Path.GetFileName).Order());

        // "Keep both" in the app is MakeUnique: the next free name.
        Assert.Equal(Path.Combine(dir, "result (2).mp4"), FileNaming.MakeUnique(existing));
        Directory.Delete(dir, true);
    }
}

public class CancellationTests(ITestOutputHelper output)
{
    /// <summary>Cancels once progress passes a point in the chosen stage, then checks nothing is left behind.</summary>
    [Theory]
    [InlineData("single pass", false, JobStage.Encoding, 0.15)]
    [InlineData("first of two passes", true, JobStage.Analysing, 0.10)]
    [InlineData("second of two passes", true, JobStage.Encoding, 0.55)]
    public async Task CancellingLeavesNoFilesAndNoProcesses(string label, bool twoPass, JobStage stage, double at)
    {
        string src = Fixtures.Get("class_code_1080p30.mp4");
        string dir = T.NewOutDir("cancel");
        var tempRoot = Path.Combine(Path.GetTempPath(), "ParadisCompress");
        var before = Directory.Exists(tempRoot) ? Directory.GetDirectories(tempRoot).ToHashSet() : [];
        int ffmpegBefore = Process.GetProcessesByName("ffmpeg").Length;

        using var cts = new CancellationTokenSource();
        JobProgress? cancelledAt = null;
        var sw = Stopwatch.StartNew();
        var options = new CompressionOptions { TargetSizeMB = twoPass ? 5 : null };
        var task = T.Compress(src, options, dir, onProgress: p =>
        {
            if (cancelledAt == null && p.Stage == stage && p.Fraction >= at)
            {
                cancelledAt = p;
                cts.Cancel();
            }
        }, cancel: cts.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        var cancelLatency = sw.Elapsed - (cancelledAt?.Elapsed ?? TimeSpan.Zero);

        Assert.NotNull(cancelledAt);
        Assert.Empty(Directory.GetFiles(dir));
        var after = Directory.Exists(tempRoot) ? Directory.GetDirectories(tempRoot).ToHashSet() : [];
        Assert.Empty(after.Except(before));
        await Task.Delay(300);
        Assert.Equal(ffmpegBefore, Process.GetProcessesByName("ffmpeg").Length);

        output.WriteLine($"{label}: cancelled at {cancelledAt!.Stage} {cancelledAt.Fraction:P0}, returned {cancelLatency.TotalMilliseconds:0} ms later");
        T.Record(nameof(CancellingLeavesNoFilesAndNoProcesses), new { label, stage = cancelledAt.Stage.ToString(), cancelledAt.Fraction, latencyMs = cancelLatency.TotalMilliseconds });

        // The same session can go straight on to a normal job.
        var again = await T.Compress(Fixtures.Get("sync_640x360_30.mp4"), new CompressionOptions(), dir);
        Assert.True(File.Exists(again.Result.OutputPath));
        Directory.Delete(dir, true);
    }
}

public class SessionTests(ITestOutputHelper output)
{
    [Fact]
    public async Task RepeatedJobsInOneSession()
    {
        string dir = T.NewOutDir("repeat");
        string src = Fixtures.Get("sync_640x360_30.mp4");
        var info = await T.Probe(src);
        var outputs = new List<string>();
        var jobs = new (CompressionOptions o, string name)[]
        {
            (new CompressionOptions(), "a"),
            (new CompressionOptions { Mode = OutputMode.AudioOnly }, "a"),
            (new CompressionOptions { Preset = PresetId.SmallestFile }, "b"),
            (new CompressionOptions { TargetSizeMB = 0.8 }, "c"),
            (new CompressionOptions(), "a (2)"),
            (new CompressionOptions { Preset = PresetId.Balanced, MixToMono = true }, "d"),
        };
        foreach (var (o, name) in jobs)
        {
            var plan = EncodePlanner.Plan(info, o);
            string path = FileNaming.MakeUnique(FileNaming.Combine(dir, name, plan.Extension));
            var sw = Stopwatch.StartNew();
            Task<JobResult> t = CompressionJob.RunAsync(T.Tools, new JobRequest(info, o, path, false), null, default);
            var returnedAfter = sw.Elapsed;
            var r = await t;
            outputs.Add(r.OutputPath);
            Assert.Equal("", T.FullDecodeErrors(r.OutputPath));
            // The call hands back a running task straight away; the work happens off the caller's thread.
            Assert.True(returnedAfter < TimeSpan.FromSeconds(1), $"RunAsync blocked the caller for {returnedAfter.TotalMilliseconds} ms");
            output.WriteLine($"{Path.GetFileName(r.OutputPath)}: {r.OutputBytes:N0} bytes; call returned after {returnedAfter.TotalMilliseconds:0} ms");
        }
        Assert.Equal(outputs.Count, outputs.Distinct().Count());
        Assert.Equal(outputs.Count, Directory.GetFiles(dir).Length);
        T.Record(nameof(RepeatedJobsInOneSession), new { outputs = outputs.Select(Path.GetFileName) });
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task ProgressIsMonotonicAndFrequent()
    {
        string dir = T.NewOutDir("progress");
        var run = await T.Compress(Fixtures.Get("class_code_1080p30.mp4"), new CompressionOptions(), dir);
        var fr = run.Progress.Select(p => p.Fraction).ToList();
        for (int i = 1; i < fr.Count; i++) Assert.True(fr[i] >= fr[i - 1] - 1e-9);
        Assert.True(fr.Count >= 10, $"only {fr.Count} progress updates");
        Assert.True(fr[^1] >= 0.99);
        Assert.Contains(run.Progress, p => p.Remaining != null);
        Assert.Contains(run.Progress, p => p.Stage == JobStage.Checking);
        // Remaining-time estimates shouldn't be wildly off once past 25 %.
        var mid = run.Progress.First(p => p.Fraction >= 0.25 && p.Remaining != null);
        double predictedEnd = (mid.Elapsed + mid.Remaining!.Value).TotalSeconds;
        double actualEnd = run.Progress[^1].Elapsed.TotalSeconds;
        output.WriteLine($"{fr.Count} updates; at 25% predicted end {predictedEnd:0.0}s, actual {actualEnd:0.0}s");
        T.Record(nameof(ProgressIsMonotonicAndFrequent), new { updates = fr.Count, predictedEnd, actualEnd });
        Directory.Delete(dir, true);
    }
}
