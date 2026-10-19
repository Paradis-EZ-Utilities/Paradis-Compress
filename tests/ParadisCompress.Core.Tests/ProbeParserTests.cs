using ParadisCompress.Core.Media;

namespace ParadisCompress.Core.Tests;

public class ProbeParserTests
{
    private const string Landscape = """
    {
      "streams": [
        { "index": 0, "codec_name": "h264", "codec_type": "video", "width": 1920, "height": 1080,
          "sample_aspect_ratio": "1:1", "avg_frame_rate": "60000/1001", "r_frame_rate": "60000/1001",
          "pix_fmt": "yuv420p", "bit_rate": "4000000", "duration": "12.512000" },
        { "index": 1, "codec_name": "aac", "codec_type": "audio", "channels": 2, "sample_rate": "48000",
          "bit_rate": "128000", "duration": "12.500000" }
      ],
      "format": { "format_name": "mov,mp4,m4a,3gp,3g2,mj2", "duration": "12.512000", "size": "6000000" }
    }
    """;

    [Fact]
    public void ReadsBasicVideoAndAudio()
    {
        var info = ProbeParser.Parse(Landscape, "x.mp4", 6_000_000);
        Assert.Equal(TimeSpan.FromSeconds(12.512), info.Duration);
        Assert.NotNull(info.Video);
        Assert.Equal(1920, info.Video!.DisplayWidth);
        Assert.Equal(1080, info.Video.DisplayHeight);
        Assert.Equal(59.94, info.Video.Fps!.Value, 2);
        Assert.False(info.Video.LooksVariableFrameRate);
        Assert.Equal(2, info.Audio!.Channels);
        Assert.Equal(48000, info.Audio.SampleRate);
        Assert.Equal(1, info.AudioStreamCount);
    }

    [Theory]
    [InlineData("-90", 270)]
    [InlineData("90", 90)]
    [InlineData("180", 180)]
    [InlineData("-180", 180)]
    [InlineData("0", 0)]
    public void DisplayMatrixRotationSwapsDisplaySize(string rotation, int expected)
    {
        string json = $$"""
        { "streams": [ { "index": 0, "codec_type": "video", "codec_name": "h264", "width": 1920, "height": 1080,
            "avg_frame_rate": "30/1", "r_frame_rate": "30/1",
            "side_data_list": [ { "side_data_type": "Display Matrix", "rotation": {{rotation}} } ] } ],
          "format": { "format_name": "mov", "duration": "5.0" } }
        """;
        var v = ProbeParser.Parse(json, "x", 1).Video!;
        Assert.Equal(expected, v.Rotation);
        bool quarter = expected is 90 or 270;
        Assert.Equal(quarter ? 1080 : 1920, v.DisplayWidth);
        Assert.Equal(quarter ? 1920 : 1080, v.DisplayHeight);
    }

    [Fact]
    public void LegacyRotateTagIsRead()
    {
        const string json = """
        { "streams": [ { "index": 0, "codec_type": "video", "width": 640, "height": 480, "tags": { "rotate": "90" } } ],
          "format": { "duration": "1.0" } }
        """;
        var v = ProbeParser.Parse(json, "x", 1).Video!;
        Assert.Equal(90, v.Rotation);
        Assert.Equal(480, v.DisplayWidth);
    }

    [Fact]
    public void CoverArtIsNotTreatedAsVideo()
    {
        const string json = """
        { "streams": [
            { "index": 0, "codec_type": "audio", "codec_name": "mp3", "channels": 2, "sample_rate": "44100" },
            { "index": 1, "codec_type": "video", "codec_name": "mjpeg", "width": 500, "height": 500, "disposition": { "attached_pic": 1 } } ],
          "format": { "format_name": "mp3", "duration": "180.0" } }
        """;
        var info = ProbeParser.Parse(json, "x.mp3", 1);
        Assert.Null(info.Video);
        Assert.NotNull(info.Audio);
    }

    [Fact]
    public void VariableFrameRateIsRecognised()
    {
        const string json = """
        { "streams": [ { "index": 0, "codec_type": "video", "width": 1280, "height": 720,
            "avg_frame_rate": "15763/1000", "r_frame_rate": "30/1" } ],
          "format": { "duration": "10.0" } }
        """;
        var v = ProbeParser.Parse(json, "x", 1).Video!;
        Assert.True(v.LooksVariableFrameRate);
        Assert.Equal(15.763, v.Fps!.Value, 3);
    }

    [Fact]
    public void HugeTimebaseRateIsIgnoredWhenAverageIsMissing()
    {
        const string json = """
        { "streams": [ { "index": 0, "codec_type": "video", "width": 1280, "height": 720,
            "avg_frame_rate": "0/0", "r_frame_rate": "90000/1" } ],
          "format": { "duration": "10.0" } }
        """;
        Assert.Null(ProbeParser.Parse(json, "x", 1).Video!.Fps);
    }

    [Fact]
    public void NonSquarePixelsGiveDisplayWidth()
    {
        const string json = """
        { "streams": [ { "index": 0, "codec_type": "video", "width": 720, "height": 576, "sample_aspect_ratio": "16:11",
            "avg_frame_rate": "25/1", "r_frame_rate": "25/1" } ],
          "format": { "duration": "10.0" } }
        """;
        var v = ProbeParser.Parse(json, "x", 1).Video!;
        Assert.True(v.HasNonSquarePixels);
        Assert.Equal(1047, v.DisplayWidth);
        Assert.Equal(576, v.DisplayHeight);
    }

    [Fact]
    public void UnknownSarIsSquare()
    {
        const string json = """
        { "streams": [ { "index": 0, "codec_type": "video", "width": 720, "height": 480, "sample_aspect_ratio": "0:1" } ],
          "format": { "duration": "1.0" } }
        """;
        Assert.False(ProbeParser.Parse(json, "x", 1).Video!.HasNonSquarePixels);
    }

    [Fact]
    public void DurationFallsBackToStreams()
    {
        const string json = """
        { "streams": [ { "index": 0, "codec_type": "audio", "channels": 1, "sample_rate": "16000", "duration": "42.5" } ],
          "format": { "format_name": "wav" } }
        """;
        Assert.Equal(42.5, ProbeParser.Parse(json, "x", 1).Duration!.Value.TotalSeconds, 3);
    }

    [Fact]
    public void MissingDurationIsNull()
    {
        const string json = """{ "streams": [ { "index": 0, "codec_type": "video", "width": 10, "height": 10 } ], "format": { "duration": "N/A" } }""";
        Assert.Null(ProbeParser.Parse(json, "x", 1).Duration);
    }

    [Fact]
    public void SecondAudioTrackIsCounted()
    {
        const string json = """
        { "streams": [
            { "index": 0, "codec_type": "audio", "channels": 2, "sample_rate": "48000" },
            { "index": 1, "codec_type": "audio", "channels": 1, "sample_rate": "48000" } ],
          "format": { "duration": "3.0" } }
        """;
        var info = ProbeParser.Parse(json, "x", 1);
        Assert.Equal(2, info.AudioStreamCount);
        Assert.Equal(0, info.Audio!.Index);
    }
}
