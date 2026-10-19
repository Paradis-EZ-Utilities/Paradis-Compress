namespace ParadisCompress.Core.Util;

public static class FileNaming
{
    /// <summary>"Lecture 5.mkv" → "Lecture 5 (compressed)" (no extension; the mode decides it).</summary>
    public static string SuggestBaseName(string inputPath, bool audioOnly)
    {
        string stem = Path.GetFileNameWithoutExtension(inputPath);
        if (string.IsNullOrWhiteSpace(stem)) stem = "Video";
        return Sanitize(stem + (audioOnly ? " (audio)" : " (compressed)"));
    }

    /// <summary>
    /// Makes a user-typed name safe as a Windows file name: strips characters Windows forbids,
    /// trailing dots/spaces, and reserved device names. Leaves spaces, Unicode, &amp;, %, ' and the like.
    /// </summary>
    public static string Sanitize(string name)
    {
        char[] invalid = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];
        var chars = name.Where(c => c >= 32 && Array.IndexOf(invalid, c) < 0).ToArray();
        string s = new string(chars).Trim().TrimEnd('.', ' ');
        if (s.Length == 0) s = "Video";
        string upper = s.Split('.')[0].ToUpperInvariant();
        string[] reserved = ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                             "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];
        if (reserved.Contains(upper)) s = "_" + s;
        if (s.Length > 180) s = s[..180].TrimEnd('.', ' ');
        return s;
    }

    /// <summary>
    /// Joins folder, base name and extension. A base name that already ends in the extension
    /// (the user typed "notes.mp4") isn't doubled.
    /// </summary>
    public static string Combine(string folder, string baseName, string extension)
    {
        string name = Sanitize(baseName);
        if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) name = name[..^extension.Length].TrimEnd('.', ' ');
        if (name.Length == 0) name = "Video";
        return Path.Combine(folder, name + extension);
    }

    /// <summary>"…\Lecture (compressed).mp4" → "…\Lecture (compressed) (2).mp4" if taken, and so on.</summary>
    public static string MakeUnique(string path, Func<string, bool>? exists = null)
    {
        exists ??= p => File.Exists(p) || Directory.Exists(p);
        if (!exists(path)) return path;
        string dir = Path.GetDirectoryName(path) ?? "";
        string stem = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);
        for (int i = 2; i < 10_000; i++)
        {
            string candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!exists(candidate)) return candidate;
        }
        return Path.Combine(dir, $"{stem} ({Guid.NewGuid():N}){ext}");
    }

    /// <summary>The hidden-in-plain-sight name the encoder writes to before the result is validated.</summary>
    public static string TemporaryOutputPath(string finalPath, string jobId)
    {
        string dir = Path.GetDirectoryName(finalPath) ?? ".";
        string stem = Path.GetFileNameWithoutExtension(finalPath);
        if (stem.Length > 120) stem = stem[..120];
        return Path.Combine(dir, $"{stem}.partial-{jobId}{Path.GetExtension(finalPath)}");
    }
}
