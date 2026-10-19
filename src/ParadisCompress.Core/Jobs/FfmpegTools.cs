namespace ParadisCompress.Core.Jobs;

/// <summary>Where ffmpeg and ffprobe live. The app ships them in an <c>ffmpeg</c> folder next to its exe.</summary>
public sealed record FfmpegTools(string FfmpegPath, string FfprobePath)
{
    /// <summary>Environment variable that points tests (or a developer) at a different FFmpeg folder.</summary>
    public const string OverrideVariable = "PARADIS_FFMPEG_DIR";

    /// <summary>
    /// Looks in, in order: <c>%PARADIS_FFMPEG_DIR%</c>, <c>&lt;app&gt;\ffmpeg\</c>, <c>&lt;app&gt;\</c>,
    /// then the PATH. Returns null when either tool is missing.
    /// </summary>
    public static FfmpegTools? Locate(string? appDirectory = null)
    {
        appDirectory ??= AppContext.BaseDirectory;
        string exe = OperatingSystem.IsWindows() ? ".exe" : "";
        var candidates = new List<string>();
        string? overrideDir = Environment.GetEnvironmentVariable(OverrideVariable);
        if (!string.IsNullOrWhiteSpace(overrideDir)) candidates.Add(overrideDir);
        candidates.Add(Path.Combine(appDirectory, "ffmpeg"));
        candidates.Add(appDirectory);

        foreach (string dir in candidates)
        {
            string ff = Path.Combine(dir, "ffmpeg" + exe), fp = Path.Combine(dir, "ffprobe" + exe);
            if (File.Exists(ff) && File.Exists(fp)) return new FfmpegTools(ff, fp);
        }

        string? pathVar = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathVar))
        {
            foreach (string dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    string ff = Path.Combine(dir, "ffmpeg" + exe), fp = Path.Combine(dir, "ffprobe" + exe);
                    if (File.Exists(ff) && File.Exists(fp)) return new FfmpegTools(ff, fp);
                }
                catch (ArgumentException)
                {
                    // A malformed PATH entry; skip it.
                }
            }
        }
        return null;
    }
}
