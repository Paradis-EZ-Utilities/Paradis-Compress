using System.Diagnostics;
using System.Globalization;
using ParadisCompress.Core.Planning;
using ParadisCompress.Core.Media;
using ParadisCompress.Core.Util;

namespace ParadisCompress.Core.Jobs;

public enum JobStage
{
    Preparing,
    Analysing,   // first of two passes
    Encoding,    // the only pass, or the second of two
    Refitting,   // pass 2 again at a lower bitrate, because the file came out above the size target
    Checking,
    Saving,
}

public sealed record JobRequest(MediaInfo Source, CompressionOptions Options, string OutputPath, bool ReplaceExisting);

public sealed record JobProgress(
    JobStage Stage,
    int Pass,
    int PassCount,
    // 0–1 across the whole job, both passes included.
    double Fraction,
    TimeSpan Elapsed,
    // Estimate only; null until there's enough to go on.
    TimeSpan? Remaining,
    // Encoding speed relative to real time (2.0 = twice as fast as playback), when known.
    double? Speed);

public sealed record JobResult(string OutputPath, long SourceBytes, long OutputBytes, MediaInfo Output, EncodePlan Plan, TimeSpan Elapsed)
{
    /// <summary>Positive = smaller (0.75 = 75 % smaller). Negative = the new file is bigger.</summary>
    public double Reduction => SourceBytes > 0 ? 1.0 - OutputBytes / (double)SourceBytes : 0;
    public bool DidNotShrink => OutputBytes >= SourceBytes;

    /// <summary>
    /// A size target was set and the finished file's actual byte count is above it, even after the
    /// corrective re-encodes. Compared in exact bytes (decimal target: 500 MB = 500,000,000 bytes).
    /// </summary>
    public bool AboveTarget => Plan.Target is { } t && OutputBytes > t.TargetBytes;

    /// <summary>
    /// Settings for "Retry smaller" after <see cref="AboveTarget"/>: the same job with a lower video
    /// bitrate (scaled by how far over the file came out, with 5 % extra margin) or the next lower MP3
    /// bitrate. Null when there was no overshoot or nothing lower is sensible.
    /// </summary>
    public CompressionOptions? RetryOptions(CompressionOptions original)
    {
        if (!AboveTarget || Plan.Target is not { } t) return null;
        if (Plan.Mode == OutputMode.AudioOnly)
            return EncodePlanner.NextLowerMp3(Plan.AudioKbps) is int lower ? original with { Mp3Kbps = lower } : null;
        if (Plan.VideoBitsPerSecond is not long bps) return null;
        double seconds = Math.Max(0.1, Plan.Duration.TotalSeconds);
        double nonVideo = (Plan.IncludeAudio ? Plan.AudioKbps * 1000.0 / 8 * seconds : 0) + 4096 + 26 * Plan.EffectiveFps * seconds;
        double factor = Math.Clamp((t.TargetBytes * 0.95 - nonVideo) / Math.Max(1, OutputBytes - nonVideo), 0.3, 0.9);
        long cap = (long)(bps * factor);
        return cap < 20_000 ? null : original with { VideoBitsPerSecondCap = cap };
    }
}

/// <summary>Checks done before starting, so the UI can ask questions instead of failing later.</summary>
public sealed record Preflight(EncodePlan Plan, bool OutputExists, long? FreeBytes, bool LowDiskSpace);

/// <summary>
/// One compression or audio-extraction job, start to finish:
/// <list type="number">
/// <item>re-check that the output can't be the input and the folder is writable,</item>
/// <item>encode into <c>name.partial-ID.mp4</c> in the destination folder (one or two passes),</item>
/// <item>validate that file with ffprobe and a decode of its start and end,</item>
/// <item>only then rename it to the chosen name.</item>
/// </list>
/// The temporary file and two-pass logs are deleted on success, failure and cancellation alike.
/// </summary>
public static class CompressionJob
{
    /// <summary>Share of a two-pass job spent in pass 1 (x264's first pass runs with fast settings and no audio).</summary>
    public const double FirstPassWeight = 0.35;

    /// <summary>
    /// x264's two-pass rate control can overshoot on near-static screen content (measured: up to +21 %
    /// on a 40 s code recording). If the file comes out above the target, pass 2 is run again with a
    /// proportionally lower bitrate, at most this many times.
    /// </summary>
    public const int MaxSizeCorrections = 3;

    /// <summary>Optional diagnostic sink (the app's log file). Never receives media content.</summary>
    public static Action<string>? Log { get; set; }

    public static Preflight Check(JobRequest req)
    {
        var plan = EncodePlanner.Plan(req.Source, req.Options);
        PathSafety.EnsureNotInput(req.Source.Path, req.OutputPath);
        bool exists = File.Exists(req.OutputPath);
        long? free = FreeBytes(Path.GetDirectoryName(Path.GetFullPath(req.OutputPath)) ?? ".");
        bool low = free is long f && f < plan.EstimatedBytes + 64 * Units.BytesPerMB;
        return new Preflight(plan, exists, free, low);
    }

    public static async Task<JobResult> RunAsync(FfmpegTools tools, JobRequest req, IProgress<JobProgress>? progress, CancellationToken cancel)
    {
        var clock = Stopwatch.StartNew();
        string jobId = Guid.NewGuid().ToString("N")[..8];
        string source = Path.GetFullPath(req.Source.Path);
        string final = Path.GetFullPath(req.OutputPath);
        string folder = Path.GetDirectoryName(final) ?? throw new CompressionException(FailureKind.DestinationNotWritable,
            "Choose a folder", "Choose a folder to save the new file in.");
        var plan = EncodePlanner.Plan(req.Source, req.Options);
        if (plan.Target?.Verdict == TargetVerdict.Impossible)
            throw new CompressionException(FailureKind.TargetTooSmall, "Target size too small", plan.Target.Message);
        if (!Path.GetExtension(final).Equals(plan.Extension, StringComparison.OrdinalIgnoreCase))
            final += plan.Extension;
        string temp = FileNaming.TemporaryOutputPath(final, jobId);
        string passDir = Path.Combine(Path.GetTempPath(), "ParadisCompress", jobId);
        bool published = false;
        int corrections = 0;

        Log?.Invoke($"Job {jobId}: {plan.Describe()}");
        Log?.Invoke($"Job {jobId}: source {req.Source.FileSizeBytes} bytes, {req.Source.Duration?.TotalSeconds:0.###} s, " +
                    $"video {req.Source.Video?.Codec} {req.Source.Video?.CodedWidth}x{req.Source.Video?.CodedHeight} rot {req.Source.Video?.Rotation} " +
                    $"fps avg {req.Source.Video?.AverageFps:0.###} base {req.Source.Video?.BaseFps:0.###}, audio {req.Source.Audio?.Codec} {req.Source.Audio?.Channels} ch");

        progress?.Report(new JobProgress(JobStage.Preparing, 0, plan.TwoPass ? 2 : 1, 0, clock.Elapsed, null, null));
        try
        {
            // Hard rules, checked again right before writing anything.
            if (!File.Exists(source))
                throw new CompressionException(FailureKind.InputMissing, "The video can't be found",
                    "The original video was moved, renamed or deleted. Choose it again.");
            PathSafety.EnsureNotInput(source, final);
            PathSafety.EnsureNotInput(source, temp);
            if (File.Exists(final) && !req.ReplaceExisting)
                throw new CompressionException(FailureKind.OutputExists, "A file with that name already exists",
                    "Choose another name, or confirm that you want to replace it.");
            EnsureWritable(folder, jobId);
            CleanStaleTempFolders();

            if (plan.TwoPass)
            {
                Directory.CreateDirectory(passDir);
                string prefix = Path.Combine(passDir, "pass");
                await RunPassAsync(tools, plan, req.Source, source, temp, 1, 2, prefix, progress, clock, cancel).ConfigureAwait(false);
                await RunPassAsync(tools, plan, req.Source, source, temp, 2, 2, prefix, progress, clock, cancel).ConfigureAwait(false);
                (plan, corrections) = await FitToTargetAsync(tools, plan, req.Source, source, temp, prefix, progress, clock, jobId, cancel).ConfigureAwait(false);
            }
            else
            {
                await RunPassAsync(tools, plan, req.Source, source, temp, 0, 1, null, progress, clock, cancel).ConfigureAwait(false);
            }

            int passes = plan.TwoPass ? 2 + corrections : 1;
            progress?.Report(new JobProgress(JobStage.Checking, passes, passes, 0.995, clock.Elapsed, null, null));
            var outInfo = await OutputValidator.ValidateAsync(tools, temp, plan, req.Source, cancel).ConfigureAwait(false);
            cancel.ThrowIfCancellationRequested();

            progress?.Report(new JobProgress(JobStage.Saving, passes, passes, 0.999, clock.Elapsed, null, null));
            PathSafety.EnsureNotInput(source, final);
            try
            {
                if (File.Exists(final) && !req.ReplaceExisting)
                    throw new CompressionException(FailureKind.OutputExists, "A file with that name already exists",
                        "A file with that name appeared while compressing. Nothing was replaced; choose another name and try again.");
                File.Move(temp, final, overwrite: req.ReplaceExisting);
                published = true;
            }
            catch (Exception ex) when (ex is not CompressionException)
            {
                throw FriendlyErrors.FromIo(ex, "save the finished file under its name");
            }

            long outBytes = new FileInfo(final).Length;
            if (plan.Target is { } tgt)
                Log?.Invoke(string.Format(CultureInfo.InvariantCulture, "Job {0}: size check: {1} bytes vs target {2} bytes: {3}",
                    jobId, outBytes, tgt.TargetBytes, outBytes <= tgt.TargetBytes ? "within target" : "ABOVE TARGET"));
            var result = new JobResult(final, req.Source.FileSizeBytes, outBytes, outInfo with { Path = final, FileSizeBytes = outBytes }, plan, clock.Elapsed);
            Log?.Invoke(string.Format(CultureInfo.InvariantCulture, "Job {0}: done in {1:0.0} s, {2} -> {3} bytes ({4:+0.0;-0.0}%)",
                jobId, clock.Elapsed.TotalSeconds, req.Source.FileSizeBytes, outBytes, -result.Reduction * 100));
            return result;
        }
        catch (OperationCanceledException)
        {
            Log?.Invoke($"Job {jobId}: cancelled after {clock.Elapsed.TotalSeconds:0.0} s");
            throw;
        }
        catch (CompressionException ex)
        {
            Log?.Invoke($"Job {jobId}: failed ({ex.Kind}) {ex.Message}" + (ex.Detail != null ? Environment.NewLine + ex.Detail : ""));
            throw;
        }
        catch (Exception ex)
        {
            Log?.Invoke($"Job {jobId}: failed unexpectedly: {ex}");
            throw FriendlyErrors.FromIo(ex, "finish the job");
        }
        finally
        {
            if (!published) TryDelete(temp);
            TryDeleteDirectory(passDir);
        }
    }

    /// <summary>
    /// Re-runs pass 2 (reusing pass 1's statistics) with a lower bitrate while the file is above the target.
    /// Returns the plan that produced the file in <paramref name="temp"/> and how many re-runs it took.
    /// </summary>
    private static async Task<(EncodePlan Plan, int Corrections)> FitToTargetAsync(FfmpegTools tools, EncodePlan plan, MediaInfo info,
        string source, string temp, string prefix, IProgress<JobProgress>? progress, Stopwatch clock, string jobId, CancellationToken cancel)
    {
        if (plan.Target is not { } target) return (plan, 0);
        double seconds = Math.Max(0.1, plan.Duration.TotalSeconds);
        // Bytes that aren't video: audio at its (constant) bitrate plus container overhead.
        double nonVideo = (plan.IncludeAudio ? plan.AudioKbps * 1000.0 / 8 * seconds : 0) + 4096 + 26 * plan.EffectiveFps * seconds;
        int corrections = 0;
        while (true)
        {
            long size = new FileInfo(temp).Length;
            if (size <= target.TargetBytes) return (plan, corrections);
            Log?.Invoke(string.Format(CultureInfo.InvariantCulture, "Job {0}: {1} bytes is {2:0.0}% of the {3}-byte target at {4} bit/s",
                jobId, size, size * 100.0 / target.TargetBytes, target.TargetBytes, plan.VideoBitsPerSecond));
            if (corrections == MaxSizeCorrections) return (plan, corrections);

            double actualVideo = Math.Max(1, size - nonVideo);
            double wantedVideo = target.TargetBytes * 0.97 - nonVideo;
            long bps = (long)(plan.VideoBitsPerSecond!.Value * Math.Clamp(wantedVideo / actualVideo, 0.3, 0.97));
            if (bps < 20_000) return (plan, corrections);
            plan = plan with { VideoBitsPerSecond = bps };
            corrections++;
            TryDelete(temp);
            await RunPassAsync(tools, plan, info, source, temp, 2, 2 + corrections, prefix, progress, clock, cancel, corrections).ConfigureAwait(false);
        }
    }

    private static async Task RunPassAsync(FfmpegTools tools, EncodePlan plan, MediaInfo info, string source, string temp,
        int pass, int passCount, string? prefix, IProgress<JobProgress>? progress, Stopwatch clock, CancellationToken cancel, int correction = 0)
    {
        var args = FfmpegArguments.Build(plan, info, source, temp, pass, prefix);
        Log?.Invoke("ffmpeg " + string.Join(' ', args.Select(Quote)));

        // Work units: pass 1 = FirstPassWeight, every run of pass 2 = 1 - FirstPassWeight. A corrective
        // re-run adds work, so the bar steps back once (labelled in the UI) rather than pretending.
        double total = passCount == 1 ? 1 : FirstPassWeight + (1 - FirstPassWeight) * (passCount - 1);
        double offset = pass == 2 ? (FirstPassWeight + (1 - FirstPassWeight) * correction) / total : 0;
        double weight = pass switch { 1 => FirstPassWeight / total, 2 => (1 - FirstPassWeight) / total, _ => 1 };
        pass = pass == 2 ? 2 + correction : pass;
        var stage = pass == 1 ? JobStage.Analysing : correction > 0 ? JobStage.Refitting : JobStage.Encoding;
        double totalSecs = plan.Duration.TotalSeconds;
        double? speed = null;
        double lastFraction = offset;

        void OnLine(string line)
        {
            int eq = line.IndexOf('=');
            if (eq <= 0) return;
            var key = line.AsSpan(0, eq);
            var val = line.AsSpan(eq + 1).Trim();
            if (key.SequenceEqual("out_time_us") || key.SequenceEqual("out_time_ms"))
            {
                // Both keys carry microseconds (out_time_ms is misnamed in FFmpeg).
                if (!long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out long us) || us < 0) return;
                double stageFraction = Math.Clamp(us / 1e6 / totalSecs, 0, 1);
                double overall = Math.Min(0.99, offset + weight * stageFraction);
                lastFraction = Math.Max(lastFraction, overall);
                progress?.Report(new JobProgress(stage, Math.Max(pass, 1), passCount, lastFraction, clock.Elapsed,
                    Estimate(clock.Elapsed, lastFraction), speed));
            }
            else if (key.SequenceEqual("speed"))
            {
                var s = val.TrimEnd('x');
                speed = double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double sp) && sp > 0 ? sp : null;
            }
        }

        var r = await FfmpegProcess.RunAsync(tools.FfmpegPath, args, cancel, OnLine).ConfigureAwait(false);
        if (r.Cancelled) throw new OperationCanceledException(cancel);
        if (!r.Succeeded)
        {
            string stageName = pass == 1 ? "the first pass" : pass >= 2 ? "the second pass" : "encoding";
            throw FriendlyErrors.FromFfmpeg(r.ErrorText, stageName);
        }
        if (r.ErrorTail.Count > 0) Log?.Invoke("ffmpeg warnings:" + Environment.NewLine + r.ErrorText);
        progress?.Report(new JobProgress(stage, Math.Max(pass, 1), passCount, Math.Min(0.99, offset + weight), clock.Elapsed,
            Estimate(clock.Elapsed, offset + weight), speed));
    }

    /// <summary>Remaining time from the average rate so far. Null early on, when it would be noise.</summary>
    internal static TimeSpan? Estimate(TimeSpan elapsed, double fraction)
    {
        if (fraction < 0.02 || elapsed.TotalSeconds < 3 || fraction >= 1) return fraction >= 1 ? TimeSpan.Zero : null;
        return TimeSpan.FromSeconds(elapsed.TotalSeconds * (1 - fraction) / fraction);
    }

    private static string Quote(string a) => a.Contains(' ') || a.Length == 0 ? "\"" + a + "\"" : a;

    public static long? FreeBytes(string folder)
    {
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(folder));
            if (string.IsNullOrEmpty(root)) return null;
            if (!OperatingSystem.IsWindows())
            {
                // Pick the mount point that actually contains the folder.
                var best = DriveInfo.GetDrives().Where(d => d.IsReady && Path.GetFullPath(folder).StartsWith(d.RootDirectory.FullName, StringComparison.Ordinal))
                    .OrderByDescending(d => d.RootDirectory.FullName.Length).FirstOrDefault();
                return best?.AvailableFreeSpace;
            }
            var drive = new DriveInfo(root);
            return drive.IsReady ? drive.AvailableFreeSpace : null;
        }
        catch (Exception)
        {
            return null; // network shares and the like: unknown, not "zero"
        }
    }

    private static void EnsureWritable(string folder, string jobId)
    {
        if (!Directory.Exists(folder))
            throw new CompressionException(FailureKind.DestinationNotWritable, "That folder no longer exists",
                "The destination folder was removed or a drive was disconnected. Choose a folder again.");
        string probe = Path.Combine(folder, $".paradis-write-test-{jobId}.tmp");
        try
        {
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
        }
        catch (Exception ex)
        {
            TryDelete(probe);
            throw FriendlyErrors.FromIo(ex, "write to that folder");
        }
    }

    /// <summary>Two-pass logs from a crashed or killed session are removed after a day.</summary>
    private static void CleanStaleTempFolders()
    {
        try
        {
            var root = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "ParadisCompress"));
            if (!root.Exists) return;
            foreach (var d in root.GetDirectories())
                if (d.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-1)) TryDeleteDirectory(d.FullName);
        }
        catch (Exception) { }
    }

    private static void TryDelete(string path)
    {
        for (int i = 0; i < 5; i++)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }
            catch (Exception)
            {
                // Windows can hold a just-killed process's file handle for a moment.
                Thread.Sleep(200);
            }
        }
        Log?.Invoke($"Could not delete temporary file {path}");
    }

    private static void TryDeleteDirectory(string path)
    {
        for (int i = 0; i < 5; i++)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception)
            {
                Thread.Sleep(200);
            }
        }
    }
}
