using ParadisCompress.Core.Jobs;
using ParadisCompress.Core.Planning;
using ParadisCompress.Core.Util;

namespace ParadisCompress.Core.Tests;

public class PresetTests
{
    /// <summary>The README documents these exact values. Change both together.</summary>
    [Fact]
    public void PresetValuesArePinned()
    {
        Assert.Equal((23, "medium", 30.0, (int?)null, 96, 10), Tuple(Presets.ClassRecording));
        Assert.Equal((26, "medium", 30.0, (int?)1080, 96, 5), Tuple(Presets.Balanced));
        Assert.Equal((30, "slow", 15.0, (int?)720, 64, 10), Tuple(Presets.SmallestFile));
        Assert.Equal(PresetId.ClassRecording, new CompressionOptions().Preset);
    }

    private static (int, string, double, int?, int, int) Tuple(PresetDefinition p) =>
        (p.Crf, p.X264Preset, p.MaxFps, p.MaxShortSide, p.AudioKbps, p.KeyframeSeconds);
}

public class FileNamingTests
{
    [Theory]
    [InlineData("C:/v/Lecture 5.mkv", false, "Lecture 5 (compressed)")]
    [InlineData("C:/v/Lecture 5.mkv", true, "Lecture 5 (audio)")]
    [InlineData("/v/été & co's #1.mp4", false, "été & co's #1 (compressed)")]
    public void Suggestion(string input, bool audio, string expected) =>
        Assert.Equal(expected, FileNaming.SuggestBaseName(input, audio));

    [Theory]
    [InlineData("a<b>c:d\"e/f\\g|h?i*j", "abcdefghij")]
    [InlineData("CON", "_CON")]
    [InlineData("nul.txt", "_nul.txt")]
    [InlineData("  name. . ", "name")]
    [InlineData("...", "Video")]
    [InlineData("50% off & 'quotes' $HOME 日本語", "50% off & 'quotes' $HOME 日本語")]
    public void Sanitize(string input, string expected) => Assert.Equal(expected, FileNaming.Sanitize(input));

    [Fact]
    public void CombineDoesNotDoubleExtension()
    {
        Assert.Equal(Path.Combine("/out", "notes.mp4"), FileNaming.Combine("/out", "notes.mp4", ".mp4"));
        Assert.Equal(Path.Combine("/out", "notes.mp4"), FileNaming.Combine("/out", "notes", ".mp4"));
    }

    [Fact]
    public void MakeUniqueCountsUp()
    {
        var taken = new HashSet<string> { "/o/a.mp4", "/o/a (2).mp4" };
        Assert.Equal(Path.Combine("/o", "a (3).mp4"), FileNaming.MakeUnique("/o/a.mp4", taken.Contains));
        Assert.Equal("/o/b.mp4", FileNaming.MakeUnique("/o/b.mp4", taken.Contains));
    }

    [Fact]
    public void TemporaryNameStaysInSameFolderWithSameExtension()
    {
        string t = FileNaming.TemporaryOutputPath("/o/Lecture.mp4", "abc12345");
        Assert.Equal(Path.Combine("/o", "Lecture.partial-abc12345.mp4"), t);
    }
}

public class PathSafetyTests
{
    [Fact]
    public void EquivalentSpellingsAreTheSameFile()
    {
        string dir = Directory.CreateTempSubdirectory("pc-safety").FullName;
        string file = Path.Combine(dir, "in.mp4");
        File.WriteAllText(file, "x");
        try
        {
            Assert.True(PathSafety.IsSameFile(file, Path.Combine(dir, ".", "in.mp4")));
            Assert.True(PathSafety.IsSameFile(file, Path.Combine(dir, "..", Path.GetFileName(dir), "in.mp4")));
            Assert.False(PathSafety.IsSameFile(file, Path.Combine(dir, "out.mp4")));
            Assert.Throws<CompressionException>(() => PathSafety.EnsureNotInput(file, Path.Combine(dir, "sub", "..", "in.mp4")));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void SymlinkToInputIsDetected()
    {
        if (OperatingSystem.IsWindows()) return; // needs developer mode on Windows; covered by file IDs there
        string dir = Directory.CreateTempSubdirectory("pc-safety").FullName;
        string file = Path.Combine(dir, "in.mp4");
        File.WriteAllText(file, "x");
        string link = Path.Combine(dir, "link.mp4");
        File.CreateSymbolicLink(link, file);
        string linkDir = Path.Combine(Path.GetTempPath(), "pc-linkdir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateSymbolicLink(linkDir, dir);
        try
        {
            Assert.True(PathSafety.IsSameFile(file, link));
            Assert.True(PathSafety.IsSameFile(file, Path.Combine(linkDir, "in.mp4")));
        }
        finally
        {
            Directory.Delete(linkDir);
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void CaseOnlyDifferenceCountsOnWindows()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.True(PathSafety.IsSameFile(@"C:\Videos\In.MP4", @"c:\videos\in.mp4"));
    }
}

public class FriendlyErrorTests
{
    [Theory]
    [InlineData("[out#0/mp4 @ 0x1] Error writing trailer: No space left on device", FailureKind.DiskFull)]
    [InlineData("av_interleaved_write_frame(): There is not enough space on the disk.", FailureKind.DiskFull)]
    [InlineData("[error] file:/x/out.mp4: Permission denied", FailureKind.DestinationNotWritable)]
    [InlineData("[error] Error opening input file /x.mp4.\nError opening input files: Invalid data found when processing input", FailureKind.InvalidInput)]
    [InlineData("[mov,mp4] moov atom not found", FailureKind.InvalidInput)]
    [InlineData("Unknown encoder 'libx264'", FailureKind.MissingEncoder)]
    [InlineData("something odd happened", FailureKind.EncodingFailed)]
    public void Classifies(string stderr, FailureKind kind) => Assert.Equal(kind, FriendlyErrors.FromFfmpeg(stderr).Kind);

    [Fact]
    public void MessagesAreFreeOfJargon()
    {
        foreach (var ex in new[] { FriendlyErrors.FromFfmpeg("x"), FriendlyErrors.DiskFull(), FriendlyErrors.InvalidInput() })
        {
            Assert.DoesNotContain("errno", ex.Message);
            Assert.DoesNotContain("0x", ex.Message);
            Assert.DoesNotContain("av_", ex.Message);
        }
    }

    [Fact]
    public void IoErrorsAreClassified()
    {
        Assert.Equal(FailureKind.DestinationNotWritable, FriendlyErrors.FromIo(new UnauthorizedAccessException("Access to the path is denied."), "x").Kind);
        Assert.Equal(FailureKind.DiskFull, FriendlyErrors.FromIo(new IOException("There is not enough space on the disk.", unchecked((int)0x80070070)), "x").Kind);
        Assert.Equal(FailureKind.DestinationNotWritable, FriendlyErrors.FromIo(new DirectoryNotFoundException(), "x").Kind);
    }
}

public class UnitsTests
{
    [Theory]
    [InlineData(1, 1_000_000)]
    [InlineData(500, 500_000_000)]       // a "500 MB" upload limit
    [InlineData(99.5, 99_500_000)]
    [InlineData(0.29, 290_000)]          // no binary rounding down to 289,999
    [InlineData(25, 25_000_000)]
    [InlineData(2000, 2_000_000_000)]
    public void MegabytesAreDecimal(double mb, long bytes) => Assert.Equal(bytes, Units.MBToBytes(mb));

    [Fact]
    public void BinaryUnitsAreOnlyMiB()
    {
        Assert.Equal(1_048_576, Units.BytesPerMiB);
        Assert.Equal(500.0, Units.BytesToMiB(500L * 1024 * 1024));
    }

    [Theory]
    [InlineData(0, "0 KB")]
    [InlineData(400, "1 KB")]
    [InlineData(812_000, "812 KB")]
    [InlineData(5_300_000, "5.3 MB")]
    [InlineData(312_000_000, "312 MB")]
    [InlineData(1_240_000_000L, "1.24 GB")]
    public void FormatSize(long bytes, string expected)
    {
        using var _ = new InvariantCultureScope();
        Assert.Equal(expected, Units.FormatSize(bytes));
    }

    [Theory]
    [InlineData(503_362_712, "503.3 MB")]   // the rc.1 report: must not read as "500 MB" or "503 MB" rounded
    [InlineData(500_000_001, "500.0 MB")]
    [InlineData(499_999_999, "499.9 MB")]
    [InlineData(24_999_999, "24.99 MB")]
    [InlineData(1_999_999, "1.99 MB")]
    [InlineData(812_345, "812.3 KB")]
    [InlineData(2_000_000_000L, "2.000 GB")]
    public void FormatSizePreciseNeverRoundsUp(long bytes, string expected)
    {
        using var _ = new InvariantCultureScope();
        Assert.Equal(expected, Units.FormatSizePrecise(bytes));
    }

    [Fact]
    public void FormatBytesIsExact()
    {
        using var _ = new InvariantCultureScope();
        Assert.Equal("503,362,712 bytes", Units.FormatBytes(503_362_712));
    }

    [Fact]
    public void Durations()
    {
        Assert.Equal("1:23:45", Units.FormatDuration(new TimeSpan(1, 23, 45)));
        Assert.Equal("4:05", Units.FormatDuration(new TimeSpan(0, 4, 5)));
        Assert.Equal("less than a minute", Units.FormatRemaining(TimeSpan.FromSeconds(30)));
        Assert.Equal("about 3 min", Units.FormatRemaining(TimeSpan.FromSeconds(150)));
        Assert.Equal("about 1 h 10 min", Units.FormatRemaining(TimeSpan.FromMinutes(70)));
    }

    [Fact]
    public void RemainingTimeIsOnlyEstimatedWithEnoughData()
    {
        Assert.Null(CompressionJob.Estimate(TimeSpan.FromSeconds(1), 0.5));
        Assert.Null(CompressionJob.Estimate(TimeSpan.FromSeconds(30), 0.01));
        Assert.Equal(30, CompressionJob.Estimate(TimeSpan.FromSeconds(30), 0.5)!.Value.TotalSeconds, 3);
    }

    private sealed class InvariantCultureScope : IDisposable
    {
        private readonly System.Globalization.CultureInfo _old = System.Globalization.CultureInfo.CurrentCulture;
        public InvariantCultureScope() => System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        public void Dispose() => System.Globalization.CultureInfo.CurrentCulture = _old;
    }
}
