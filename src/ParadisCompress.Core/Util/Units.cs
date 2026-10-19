using System.Globalization;

namespace ParadisCompress.Core.Util;

/// <summary>
/// Size and time formatting. Paradis Compress uses <b>decimal units</b>, the way upload limits are
/// normally stated: <b>1 KB = 1,000 bytes, 1 MB = 1,000,000 bytes, 1 GB = 1,000,000,000 bytes</b>.
/// Every size the app shows and every target it accepts uses these units. (Windows File Explorer
/// counts in binary units of 1,048,576 bytes but also labels them "MB", so it shows a smaller number
/// for the same file. Where the app mentions binary units, it labels them MiB.)
/// </summary>
public static class Units
{
    public const long BytesPerKB = 1_000;
    public const long BytesPerMB = 1_000_000;
    public const long BytesPerGB = 1_000_000_000;
    public const long BytesPerMiB = 1024 * 1024;

    /// <summary>Exact decimal conversion: 500 MB → 500,000,000 bytes, 99.5 MB → 99,500,000 bytes.</summary>
    public static long MBToBytes(double mb)
    {
        if (double.IsNaN(mb) || mb <= 0) return 0;
        if (mb >= long.MaxValue / BytesPerMB) return long.MaxValue;
        // decimal, so 0.29 MB is 290,000 bytes and not 289,999 after binary rounding.
        return (long)Math.Floor((decimal)mb * BytesPerMB);
    }

    public static double BytesToMB(long bytes) => bytes / (double)BytesPerMB;

    public static double BytesToMiB(long bytes) => bytes / (double)BytesPerMiB;

    /// <summary>"812 KB", "4.7 MB", "312 MB", "1.24 GB" (decimal units).</summary>
    public static string FormatSize(long bytes)
    {
        var c = CultureInfo.CurrentCulture;
        if (bytes < 0) bytes = 0;
        if (bytes < BytesPerMB) return string.Format(c, "{0:0} KB", Math.Max(bytes > 0 ? 1 : 0, Math.Round(bytes / (double)BytesPerKB)));
        double mb = BytesToMB(bytes);
        if (mb < 10) return string.Format(c, "{0:0.0} MB", mb);
        if (mb < 1000) return string.Format(c, "{0:0} MB", mb);
        return string.Format(c, "{0:0.00} GB", bytes / (double)BytesPerGB);
    }

    /// <summary>
    /// Like <see cref="FormatSize"/> but never rounds up past a limit: 503,362,712 bytes is "503.4 MB",
    /// not "503 MB", so a result just above a 500 MB target can't look as if it met it.
    /// </summary>
    public static string FormatSizePrecise(long bytes)
    {
        var c = CultureInfo.CurrentCulture;
        if (bytes < 0) bytes = 0;
        if (bytes < BytesPerMB) return string.Format(c, "{0:0.#} KB", Math.Floor(bytes / (double)BytesPerKB * 10) / 10);
        double mb = Math.Floor(BytesToMB(bytes) * 100) / 100;
        if (mb < 1000) return string.Format(c, mb < 100 ? "{0:0.00} MB" : "{0:0.0} MB", mb < 100 ? mb : Math.Floor(mb * 10) / 10);
        return string.Format(c, "{0:0.000} GB", Math.Floor(bytes / (double)BytesPerGB * 1000) / 1000);
    }

    /// <summary>"503,362,712 bytes".</summary>
    public static string FormatBytes(long bytes) => string.Format(CultureInfo.CurrentCulture, "{0:N0} bytes", bytes);

    /// <summary>A target size in bytes, shown the way it was typed: 500,000,000 → "500 MB", 99,500,000 → "99.5 MB".</summary>
    public static string FormatTargetBytes(long bytes) => FormatTargetMB(bytes / (double)BytesPerMB);

    /// <summary>A size target as typed: "500 MB", "99.5 MB".</summary>
    public static string FormatTargetMB(double mb) => string.Format(CultureInfo.CurrentCulture, "{0:0.###} MB", mb);

    /// <summary>"1:23:45" or "4:05".</summary>
    public static string FormatDuration(TimeSpan d)
    {
        if (d < TimeSpan.Zero) d = TimeSpan.Zero;
        return d.TotalHours >= 1
            ? $"{(int)d.TotalHours}:{d.Minutes:00}:{d.Seconds:00}"
            : $"{d.Minutes}:{d.Seconds:00}";
    }

    /// <summary>"about 3 min", "about 1 h 10 min", "less than a minute".</summary>
    public static string FormatRemaining(TimeSpan d)
    {
        if (d.TotalSeconds < 60) return "less than a minute";
        if (d.TotalHours >= 1) return $"about {(int)d.TotalHours} h {d.Minutes} min";
        return $"about {(int)Math.Ceiling(d.TotalMinutes)} min";
    }

    /// <summary>"30 fps", "29.97 fps", "unknown fps".</summary>
    public static string FormatFps(double? fps)
    {
        if (fps is not > 0) return "unknown frame rate";
        double r = Math.Round(fps.Value);
        return Math.Abs(fps.Value - r) < 0.01
            ? string.Format(CultureInfo.CurrentCulture, "{0:0} fps", r)
            : string.Format(CultureInfo.CurrentCulture, "{0:0.##} fps", fps.Value);
    }
}
