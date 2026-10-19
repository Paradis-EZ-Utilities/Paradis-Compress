using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using ParadisCompress.Core.Jobs;
using ParadisCompress.Core.Media;
using ParadisCompress.Core.Planning;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ParadisCompress.IntegrationTests;

/// <summary>Shared helpers: the FFmpeg under test, running jobs, measuring outputs, recording results.</summary>
public static class T
{
    public static readonly FfmpegTools Tools = FfmpegTools.Locate()
        ?? throw new InvalidOperationException($"Set {FfmpegTools.OverrideVariable} to the folder holding ffmpeg and ffprobe.");

    public static readonly string Artifacts = Environment.GetEnvironmentVariable("PARADIS_TEST_ARTIFACTS")
        ?? Path.Combine(Path.GetTempPath(), "paradis-compress-artifacts");

    private static readonly object ResultsLock = new();

    public static string NewOutDir(string name)
    {
        string d = Path.Combine(Path.GetTempPath(), "paradis-compress-tests", name + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(d);
        return d;
    }

    public static Task<MediaInfo> Probe(string path) => MediaProber.ProbeAsync(Tools, path);

    public sealed record Run(JobResult Result, List<JobProgress> Progress, TimeSpan Wall);

    public static async Task<Run> Compress(string input, CompressionOptions options, string outDir, string? name = null,
        bool replace = false, CancellationToken cancel = default, Action<JobProgress>? onProgress = null)
    {
        var info = await Probe(input);
        var plan = EncodePlanner.Plan(info, options);
        string output = Path.Combine(outDir, (name ?? Path.GetFileNameWithoutExtension(input) + "-out") + plan.Extension);
        var list = new List<JobProgress>();
        var progress = new SyncProgress<JobProgress>(p => { lock (list) list.Add(p); onProgress?.Invoke(p); });
        var sw = Stopwatch.StartNew();
        var result = await CompressionJob.RunAsync(Tools, new JobRequest(info, options, output, replace), progress, cancel);
        return new Run(result, list, sw.Elapsed);
    }

    /// <summary>IProgress that calls back immediately on the reporting thread (Progress&lt;T&gt; would post to the thread pool).</summary>
    public sealed class SyncProgress<TP>(Action<TP> a) : IProgress<TP>
    {
        public void Report(TP value) => a(value);
    }

    public static string Sha256(string path)
    {
        using var s = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(s));
    }

    public sealed record ProcOut(int Exit, string Out, string Err);

    public static ProcOut Exec(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (string a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        string o = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return new ProcOut(p.ExitCode, o, err.Result);
    }

    /// <summary>Decodes every frame and sample of a file; returns FFmpeg's error output (empty = clean).</summary>
    public static string FullDecodeErrors(string path)
    {
        var r = Exec(Tools.FfmpegPath, "-hide_banner", "-nostdin", "-v", "error", "-i", "file:" + path, "-f", "null", "-");
        return r.Exit == 0 ? r.Err.Trim() : $"exit {r.Exit}: {r.Err}";
    }

    /// <summary>Frame count and exact frame timestamps of the first video stream.</summary>
    public static List<double> FrameTimes(string path)
    {
        var r = Exec(Tools.FfprobePath, "-v", "error", "-select_streams", "v:0", "-show_entries", "frame=best_effort_timestamp_time",
            "-of", "csv=p=0", "file:" + path);
        return r.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => double.Parse(l.TrimEnd(','), CultureInfo.InvariantCulture)).ToList();
    }

    // ------------------------------------------------------------------ A/V sync markers

    /// <summary>Times the corner square turns white (video) and the beep starts (audio).</summary>
    public static (List<double> video, List<double> audio) SyncMarkers(string path, double? from = null, double? length = null)
    {
        var pre = new List<string> { "-hide_banner", "-nostdin" };
        if (from is double f) pre.AddRange(["-ss", f.ToString(CultureInfo.InvariantCulture)]);
        if (length is double l) pre.AddRange(["-t", l.ToString(CultureInfo.InvariantCulture)]);
        pre.AddRange(["-i", "file:" + path]);

        var vr = Exec(Tools.FfmpegPath, [.. pre, "-an", "-vf",
            "crop=w=iw*0.05:h=ih*0.08:x=iw*0.03:y=ih*0.05,signalstats,metadata=mode=print:key=lavfi.signalstats.YAVG:file=-",
            "-f", "null", "-"]);
        var video = new List<double>();
        double? t = null;
        double prev = 0;
        foreach (string line in vr.Out.Split('\n'))
        {
            var m = Regex.Match(line, @"pts_time:([0-9.]+)");
            if (m.Success) { t = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture); continue; }
            var y = Regex.Match(line, @"YAVG=([0-9.]+)");
            if (y.Success && t != null)
            {
                double v = double.Parse(y.Groups[1].Value, CultureInfo.InvariantCulture);
                if (v > 128 && prev <= 128) video.Add(t.Value + (from ?? 0));
                prev = v;
            }
        }

        var ar = Exec(Tools.FfmpegPath, [.. pre, "-vn", "-af", "silencedetect=n=-30dB:d=0.05", "-f", "null", "-"]);
        var audio = Regex.Matches(ar.Err, @"silence_end: ([0-9.]+)")
            .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) + (from ?? 0)).ToList();
        return (video, audio);
    }

    /// <summary>Audio onset minus video onset for every marker (positive = sound late).</summary>
    public static List<double> SyncOffsets(string path, double? from = null, double? length = null)
    {
        var (video, audio) = SyncMarkers(path, from, length);
        var offsets = new List<double>();
        foreach (double v in video)
        {
            if (audio.Count == 0) break;
            double a = audio.MinBy(x => Math.Abs(x - v));
            if (Math.Abs(a - v) < 0.5) offsets.Add(a - v);
        }
        return offsets;
    }

    // ------------------------------------------------------------------ picture comparison

    /// <summary>Mean SSIM and PSNR of output vs source, scaling the output back to the source size when needed.</summary>
    public static (double ssim, double psnr) Quality(string output, string source, int srcW, int srcH)
    {
        var r = Exec(Tools.FfmpegPath, "-hide_banner", "-nostdin", "-i", "file:" + output, "-i", "file:" + source,
            "-lavfi", $"[0:v]scale={srcW}:{srcH}:flags=bicubic,setsar=1,format=yuv420p[a];[1:v]setsar=1,format=yuv420p[b];[a][b]ssim", "-f", "null", "-");
        var p = Exec(Tools.FfmpegPath, "-hide_banner", "-nostdin", "-i", "file:" + output, "-i", "file:" + source,
            "-lavfi", $"[0:v]scale={srcW}:{srcH}:flags=bicubic,setsar=1,format=yuv420p[a];[1:v]setsar=1,format=yuv420p[b];[a][b]psnr", "-f", "null", "-");
        double ssim = double.Parse(Regex.Match(r.Err, @"All:([0-9.]+)").Groups[1].Value, CultureInfo.InvariantCulture);
        var pm = Regex.Match(p.Err, @"average:([0-9.]+|inf)");
        double psnr = pm.Groups[1].Value == "inf" ? 99 : double.Parse(pm.Groups[1].Value, CultureInfo.InvariantCulture);
        return (ssim, psnr);
    }

    /// <summary>Saves a PNG of one frame at native resolution (uses the generator FFmpeg, which has PNG support).</summary>
    public static string SaveFrame(string video, double seconds, string pngPath, string? crop = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(pngPath)!);
        var args = new List<string> { "-hide_banner", "-nostdin", "-y", "-v", "error", "-ss", seconds.ToString(CultureInfo.InvariantCulture),
            "-i", video, "-frames:v", "1" };
        if (crop != null) args.AddRange(["-vf", crop]);
        args.Add(pngPath);
        var r = Exec(Fixtures.Generator, [.. args]);
        if (r.Exit != 0) throw new InvalidOperationException(r.Err);
        return pngPath;
    }

    // ------------------------------------------------------------------ results log for the test report

    public static void Record(string test, object data)
    {
        Directory.CreateDirectory(Artifacts);
        string line = JsonSerializer.Serialize(new { test, at = DateTime.UtcNow, data });
        lock (ResultsLock) File.AppendAllText(Path.Combine(Artifacts, "results.jsonl"), line + "\n");
    }

    public static bool IsRoot => !OperatingSystem.IsWindows() && Environment.UserName == "root";
}
