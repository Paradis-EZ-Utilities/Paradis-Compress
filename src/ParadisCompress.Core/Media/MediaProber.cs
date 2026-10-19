using System.Text.Json;
using ParadisCompress.Core.Planning;
using ParadisCompress.Core.Jobs;
using ParadisCompress.Core.Util;

namespace ParadisCompress.Core.Media;

/// <summary>Runs ffprobe on a file and explains failures in plain language.</summary>
public static class MediaProber
{
    public static async Task<MediaInfo> ProbeAsync(FfmpegTools tools, string path, CancellationToken cancel = default)
    {
        string full = Path.GetFullPath(path);
        if (Directory.Exists(full))
            throw new CompressionException(FailureKind.InvalidInput, "That's a folder", "Choose a video file rather than a folder.");
        FileInfo fi;
        try
        {
            fi = new FileInfo(full);
            if (!fi.Exists)
                throw new CompressionException(FailureKind.InputMissing, "The file can't be found",
                    "That file doesn't exist any more. It may have been moved, renamed or deleted.");
            if (fi.Length == 0) throw FriendlyErrors.InvalidInput("File is empty (0 bytes).");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new CompressionException(FailureKind.InvalidInput, "The file can't be opened",
                "Windows didn't allow Paradis Compress to read this file. If it's still being recorded or copied, wait until that finishes.", ex.ToString(), ex);
        }

        var result = await FfmpegProcess.RunAsync(tools.FfprobePath, FfmpegArguments.Probe(full), cancel, captureStdout: true)
            .ConfigureAwait(false);
        cancel.ThrowIfCancellationRequested();
        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.StandardOutput))
            throw FriendlyErrors.InvalidInput($"ffprobe exit {result.ExitCode}: {result.ErrorText}");

        MediaInfo info;
        try
        {
            info = ProbeParser.Parse(result.StandardOutput, full, fi.Length);
        }
        catch (JsonException ex)
        {
            throw FriendlyErrors.InvalidInput("ffprobe output was not valid JSON: " + ex.Message);
        }

        // A single picture (a PNG or JPEG chosen by mistake) probes as a one-frame "video".
        if (info.FormatName == "image2" || info.FormatName.EndsWith("_pipe", StringComparison.Ordinal))
            throw new CompressionException(FailureKind.InvalidInput, "That's a picture, not a video",
                "Choose a video file, such as an .mp4, .mkv, .mov, .webm or .avi recording.");
        if (!info.HasVideo && !info.HasAudio)
            throw FriendlyErrors.InvalidInput("No video or audio streams found. Format: " + info.FormatName);
        if (info.Duration == null)
            throw FriendlyErrors.InvalidInput("Duration unknown. Format: " + info.FormatName);
        return info;
    }
}
