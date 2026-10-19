namespace ParadisCompress.Core.Util;

public enum FailureKind
{
    InvalidInput,
    InputMissing,
    MissingTools,
    MissingEncoder,
    DestinationNotWritable,
    DiskFull,
    OutputIsInput,
    OutputExists,
    EncodingFailed,
    ValidationFailed,
    TargetTooSmall,
}

/// <summary>A failure explained in one or two plain sentences, with the technical detail kept for the log.</summary>
public sealed class CompressionException : Exception
{
    public FailureKind Kind { get; }

    /// <summary>The short heading shown in the error box, e.g. "Not enough disk space".</summary>
    public string Title { get; }

    /// <summary>Technical detail (FFmpeg's last error lines, exception text). Goes to the log, not the user.</summary>
    public string? Detail { get; }

    public CompressionException(FailureKind kind, string title, string message, string? detail = null, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
        Title = title;
        Detail = detail;
    }
}

/// <summary>Maps FFmpeg's error output and .NET IO exceptions to something a person can act on.</summary>
public static class FriendlyErrors
{
    public static CompressionException FromFfmpeg(string errorText, string? stage = null)
    {
        string t = errorText ?? "";
        bool Has(string s) => t.Contains(s, StringComparison.OrdinalIgnoreCase);

        if (Has("No space left on device") || Has("not enough space on the disk") || Has("disk full"))
            return DiskFull(t);
        if (Has("Permission denied") || Has("Access is denied") || Has("Read-only file system"))
            return new CompressionException(FailureKind.DestinationNotWritable, "Can't write to that folder",
                "Windows didn't let Paradis Compress write the new file there. Choose another folder, such as your Videos or Documents folder.", t);
        if (Has("Unknown encoder") || Has("Encoder not found") || Has("Unrecognized option 'x264")
            || Has("Error selecting an encoder"))
            return new CompressionException(FailureKind.MissingEncoder, "A required encoder is missing",
                "The copy of FFmpeg that came with Paradis Compress can't create this kind of file. Reinstalling (unzipping) Paradis Compress again should fix this.", t);
        if (Has("Invalid data found when processing input") || Has("moov atom not found")
            || Has("could not find codec parameters") || Has("does not contain any stream")
            || Has("Invalid argument") && Has("Error opening input"))
            return InvalidInput(t);
        if (Has("No such file or directory") && Has("Error opening input"))
            return new CompressionException(FailureKind.InputMissing, "The video can't be found",
                "The original video was moved, renamed or deleted. Choose it again.", t);
        return new CompressionException(FailureKind.EncodingFailed, "Compression didn't finish",
            "FFmpeg stopped with an error" + (stage != null ? $" during {stage}" : "") +
            ". The original file is untouched and no new file was saved. The log has the technical details.", t);
    }

    public static CompressionException InvalidInput(string? detail = null) =>
        new(FailureKind.InvalidInput, "This file can't be read",
            "This doesn't look like a video or audio file Paradis Compress can read, or the file is damaged or incomplete (for example, a recording that was still being written).",
            detail);

    public static CompressionException DiskFull(string? detail = null) =>
        new(FailureKind.DiskFull, "Not enough disk space",
            "The destination drive ran out of space. Free up some space or choose a folder on another drive, then try again. Nothing was saved.",
            detail);

    /// <summary>Classifies an exception thrown by our own file operations (temp file, final rename).</summary>
    public static CompressionException FromIo(Exception ex, string action)
    {
        if (ex is CompressionException ce) return ce;
        const int ERROR_HANDLE_DISK_FULL = 0x27, ERROR_DISK_FULL = 0x70;
        int code = ex.HResult & 0xFFFF;
        if (code is ERROR_HANDLE_DISK_FULL or ERROR_DISK_FULL || ex.Message.Contains("space", StringComparison.OrdinalIgnoreCase))
            return DiskFull(ex.ToString());
        if (ex is UnauthorizedAccessException || ex.Message.Contains("denied", StringComparison.OrdinalIgnoreCase))
            return new CompressionException(FailureKind.DestinationNotWritable, "Can't write to that folder",
                $"Windows didn't allow Paradis Compress to {action}. Choose another folder, or close any program that has the file open.", ex.ToString(), ex);
        if (ex is DirectoryNotFoundException)
            return new CompressionException(FailureKind.DestinationNotWritable, "That folder no longer exists",
                "The destination folder was removed or a drive was disconnected. Choose a folder again.", ex.ToString(), ex);
        if (ex is IOException)
            return new CompressionException(FailureKind.DestinationNotWritable, "The file couldn't be saved",
                $"Paradis Compress couldn't {action}. If the file is open in another program, close it and try again.", ex.ToString(), ex);
        return new CompressionException(FailureKind.EncodingFailed, "Something went wrong",
            $"Paradis Compress couldn't {action}. The log has the technical details.", ex.ToString(), ex);
    }
}
