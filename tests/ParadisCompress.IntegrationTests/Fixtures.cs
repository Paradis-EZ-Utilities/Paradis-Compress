using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace ParadisCompress.IntegrationTests;

/// <summary>
/// Generates every test video from scratch with FFmpeg's built-in sources (lavfi), so the tests need
/// no downloaded footage and every run uses identical inputs.
///
/// The generator is a separate, full-featured FFmpeg (it needs drawtext + fonts and flite for
/// synthetic speech), set with PARADIS_FIXTURE_FFMPEG (default: "ffmpeg" on PATH). The app under test
/// uses the bundled-version FFmpeg from PARADIS_FFMPEG_DIR. Fixtures are cached in PARADIS_FIXTURE_DIR
/// (default: &lt;temp&gt;/paradis-compress-fixtures) keyed by a version number; bump it after changing a recipe.
/// </summary>
public sealed class Fixtures
{
    private const int RecipeVersion = 3;

    public static readonly string Generator = Environment.GetEnvironmentVariable("PARADIS_FIXTURE_FFMPEG") ?? "ffmpeg";
    public static readonly string Dir = Path.Combine(
        Environment.GetEnvironmentVariable("PARADIS_FIXTURE_DIR") ?? Path.Combine(Path.GetTempPath(), "paradis-compress-fixtures"),
        "v" + RecipeVersion);

    private const string Mono = "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf";
    private const string Sans = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf";
    private const string SansBold = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf";

    // Re-entrant: some recipes are built from other fixtures.
    private static readonly object Gate = new();

    /// <summary>Returns the path of a fixture, generating it first if needed.</summary>
    public static string Get(string name)
    {
        Directory.CreateDirectory(Dir);
        string path = Path.Combine(Dir, name);
        if (File.Exists(path)) return path;
        lock (Gate)
        {
            if (File.Exists(path)) return path;
            string tmp = Path.Combine(Dir, "tmp-" + name);
            Generate(name, tmp);
            File.Move(tmp, path, true);
            return path;
        }
    }

    private static void Generate(string name, string output)
    {
        switch (name)
        {
            case "class_code_1080p30.mp4": ClassCode(output); break;
            case "slides_720p25.mp4": Slides(output, 1280, 720, 25, 40, "-crf", "16", "-preset", "veryfast", "-b:a", "160k", stereo: true); break;
            case "precompressed_slides_720p25.mp4": Slides(output, 1280, 720, 25, 40, "-crf", "32", "-preset", "veryslow", "-b:a", "48k", stereo: false); break;
            case "mono_speech_640x360.mp4": Slides(output, 640, 360, 25, 15, "-crf", "20", "-preset", "veryfast", "-b:a", "96k", stereo: false); break;
            case "motion_720p30.mp4": Motion(output); break;
            case "sync_640x360_30.mp4": Sync(output, 640, 360, 30, 20); break;
            case "hfr_1280x720_60.mp4": Sync(output, 1280, 720, 60, 16); break;
            case "noaudio_720p30.mp4":
                Run("-f", "lavfi", "-i", "testsrc2=s=1280x720:r=30:d=10", "-c:v", "libx264", "-preset", "veryfast", "-crf", "18",
                    "-pix_fmt", "yuv420p", output);
                break;
            case "odd_641x361.mkv": Odd(output); break;
            case "rotated_90.mp4": Rotated(output); break;
            case "vfr_low_sync.mp4": Vfr(output, "gte(mod(t\\,2)\\,0.95)*lte(mod(t\\,2)\\,1.1)+not(mod(n\\,5))+between(t\\,6\\,9)"); break;
            case "vfr_high_sync.mp4": Vfr(output, "mod(n\\,7)"); break;
            case "surround_51.mkv":
                Run("-f", "lavfi", "-i", "testsrc2=s=320x240:r=30:d=10",
                    "-f", "lavfi", "-i", "aevalsrc=sin(440*2*PI*t)|sin(550*2*PI*t)|sin(660*2*PI*t)|0.2*sin(60*2*PI*t)|sin(770*2*PI*t)|sin(880*2*PI*t):s=48000:c=5.1:d=10",
                    "-c:v", "libx264", "-preset", "veryfast", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "256k", output);
                break;
            case "long_60min_360p15.mp4": Sync(output, 640, 360, 15, 3600, fast: true); break;
            case "notvideo.mp4": File.WriteAllText(output, "This is a text file with a video extension.\n"); break;
            case "empty.mp4": File.WriteAllBytes(output, []); break;
            case "picture.png":
                Run("-f", "lavfi", "-i", "testsrc2=s=640x360", "-frames:v", "1", "-f", "image2", "-c:v", "png", output);
                break;
            case "truncated.mp4":
                byte[] all = File.ReadAllBytes(Get("noaudio_720p30.mp4"));
                File.WriteAllBytes(output, all[..(all.Length * 4 / 10)]);
                break;
            default: throw new ArgumentException("Unknown fixture " + name);
        }
    }

    // ------------------------------------------------------------------ recipes

    /// <summary>
    /// A 1080p screen recording of a code lecture: dark editor with 18 px code that scrolls a page
    /// every 10 s, a 13 px terminal panel, a 1 px-wide underline, a moving webcam picture-in-picture
    /// and synthetic speech. Encoded at high quality, like an OBS/Zoom recording kept at good quality.
    /// </summary>
    private static void ClassCode(string output)
    {
        string dir = WorkDir();
        string code = Path.Combine(dir, "code.txt");
        var sb = new StringBuilder();
        string[] block =
        [
            "def compute_average(values: list[int]) -> float:",
            "    \"\"\"Return the mean; raise ValueError on an empty list.\"\"\"",
            "    if not values:",
            "        raise ValueError(\"values must not be empty\")",
            "    total = 0",
            "    for i, v in enumerate(values):   # l1I|0O8B distinct?",
            "        total += v",
            "    return total / len(values)",
            "",
            "class Node:",
            "    def __init__(self, key, left=None, right=None):",
            "        self.key, self.left, self.right = key, left, right",
            "",
            "def insert(root: Node | None, key: int) -> Node:",
            "    if root is None: return Node(key)",
            "    if key < root.key: root.left = insert(root.left, key)",
            "    else:              root.right = insert(root.right, key)",
            "    return root",
            "",
        ];
        for (int page = 0; page < 6; page++)
        {
            sb.AppendLine($"# ---- section {page + 1}: binary search trees (page {page + 1}/6) ----");
            foreach (string line in block) sb.AppendLine(line.Replace("key", page % 2 == 0 ? "key" : "k" + page));
        }
        File.WriteAllText(code, sb.ToString());
        string term = Path.Combine(dir, "term.txt");
        File.WriteAllText(term, string.Join('\n',
            "$ python -m pytest tests/test_bst.py -q",
            "........F...                                  [100%]",
            "FAILED tests/test_bst.py::test_delete_leaf - AssertionError",
            "  assert inorder(t) == [1, 3, 4, 7, 9]",
            "  where [1, 3, 4, 7, 9, 12] = inorder(<Node 7>)",
            "1 failed, 11 passed in 0.08s",
            "$ git diff --stat",
            " src/bst.py | 14 +++++++++-----",
            " 1 file changed, 9 insertions(+), 5 deletions(-)"));
        string title = Path.Combine(dir, "title.txt");
        File.WriteAllText(title, "lecture_07.py  -  Data Structures (CS 201)  -  Week 7");
        string speech = Speech(dir, 40);

        string vf =
            $"[0]drawbox=x=0:y=0:w=1920:h=44:color=0x2d2d2d:t=fill," +
            $"drawtext=fontfile={Mono}:textfile={title}:expansion=none:fontsize=20:fontcolor=0xcccccc:x=20:y=12," +
            $"drawtext=fontfile={Mono}:textfile={code}:expansion=none:fontsize=18:line_spacing=8:fontcolor=0xd4d4d4:x=40:y=70-416*floor(t/10)," +
            $"drawbox=x=0:y=44:w=1920:h=16:color=0x1e1e1e:t=fill," +
            $"drawbox=x=1180:y=600:w=700:h=440:color=0x0c0c0c:t=fill," +
            $"drawtext=fontfile={Mono}:textfile={term}:expansion=none:fontsize=13:line_spacing=6:fontcolor=0x9cdcfe:x=1195:y=612," +
            $"drawbox=x=1195:y=1010:w=660:h=1:color=0xffcc00:t=fill[bg];" +
            $"[1]drawbox=x=0:y=0:w=320:h=180:color=white:t=2[pip];" +
            $"[bg][pip]overlay=x='1560+20*sin(t)':y=70:shortest=1[v];" +
            $"[2]aresample=48000,pan=stereo|c0=c0|c1=0.9*c0,apad[a]";
        Run("-f", "lavfi", "-i", "color=c=0x1e1e1e:s=1920x1080:r=30:d=40",
            "-f", "lavfi", "-i", "testsrc2=s=320x180:r=30:d=40",
            "-f", "lavfi", "-i", $"flite=textfile={speech}:voice=slt",
            "-filter_complex", vf, "-map", "[v]", "-map", "[a]", "-t", "40",
            "-c:v", "libx264", "-preset", "veryfast", "-crf", "14", "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-b:a", "192k", output);
    }

    private static void Slides(string output, int w, int h, int fps, int secs, string crfFlag, string crf, string presetFlag,
        string preset, string abFlag, string ab, bool stereo)
    {
        string dir = WorkDir();
        double s = h / 720.0;
        int F(double px) => Math.Max(8, (int)Math.Round(px * s));
        var filters = new List<string>();
        string[][] slides =
        [
            ["Week 7: Sorting Algorithms", "• Comparison sorts: O(n log n) lower bound\n• Merge sort: stable, O(n) extra space\n• Quick sort: in-place, O(n²) worst case\n• Heap sort: in-place, not stable"],
            ["Merge Sort - Recurrence", "T(n) = 2·T(n/2) + Θ(n)\nT(1) = Θ(1)\n=> T(n) = Θ(n log n)   (Master theorem, case 2)\n\nExample: [38, 27, 43, 3, 9, 82, 10]"],
            ["Comparison Table", "Algorithm    Best        Average     Worst       Stable\nMerge        n log n     n log n     n log n     yes\nQuick        n log n     n log n     n²          no\nHeap         n log n     n log n     n log n     no\nInsertion    n           n²          n²          yes"],
            ["Questions?", "Office hours: Tue 14:00-15:30, Room B-214\nAssignment 4 due Friday 23:59\nRead: CLRS ch. 6-8"],
        ];
        for (int i = 0; i < slides.Length; i++)
        {
            string tf = Path.Combine(dir, $"title{i}.txt"), bf = Path.Combine(dir, $"body{i}.txt");
            File.WriteAllText(tf, slides[i][0]);
            File.WriteAllText(bf, slides[i][1]);
            double from = i * secs / (double)slides.Length, to = (i + 1) * secs / (double)slides.Length;
            string en = $"enable='between(t,{from.ToString(CultureInfo.InvariantCulture)},{to.ToString(CultureInfo.InvariantCulture)})'";
            filters.Add($"drawtext=fontfile={SansBold}:textfile={tf}:expansion=none:fontsize={F(44)}:fontcolor=0x1a1a1a:x={F(60)}:y={F(50)}:{en}");
            filters.Add($"drawtext=fontfile={(i == 2 ? Mono : Sans)}:textfile={bf}:expansion=none:fontsize={F(i == 2 ? 20 : 26)}:line_spacing={F(14)}:fontcolor=0x222222:x={F(80)}:y={F(150)}:{en}");
        }
        string foot = Path.Combine(dir, "foot.txt");
        File.WriteAllText(foot, "CS 201 Data Structures - Fall 2026 - slide footnote in 13 px text");
        filters.Add($"drawbox=x=0:y={h - F(40)}:w={w}:h=1:color=0x999999:t=fill");
        filters.Add($"drawtext=fontfile={Sans}:textfile={foot}:expansion=none:fontsize={F(13)}:fontcolor=0x555555:x={F(60)}:y={h - F(28)}");
        string speech = Speech(dir, secs);
        string pan = stereo ? "pan=stereo|c0=c0|c1=0.9*c0" : "pan=mono|c0=c0";
        Run("-f", "lavfi", "-i", $"color=c=0xf5f5f0:s={w}x{h}:r={fps}:d={secs}",
            "-f", "lavfi", "-i", $"flite=textfile={speech}:voice=slt",
            "-filter_complex", $"[0]{string.Join(',', filters)}[v];[1]aresample=48000,{pan},apad[a]",
            "-map", "[v]", "-map", "[a]", "-t", secs.ToString(CultureInfo.InvariantCulture),
            "-c:v", "libx264", presetFlag, preset, crfFlag, crf, "-pix_fmt", "yuv420p", "-c:a", "aac", abFlag, ab, output);
    }

    private static void Motion(string output)
    {
        Run("-f", "lavfi", "-i", "testsrc2=s=1280x720:r=30:d=20",
            "-f", "lavfi", "-i", "aevalsrc=0.4*sin(440*2*PI*t)|0.4*sin(660*2*PI*t):s=48000:d=20",
            "-vf", "noise=alls=14:allf=t+u,hue=H=0.4*t",
            "-c:v", "libx264", "-preset", "veryfast", "-crf", "16", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "160k", output);
    }

    /// <summary>
    /// Moving picture plus sync markers: every 2 s (at t = 1, 3, 5, …) a white square flashes in the
    /// top-left corner for ~1.5 frames and a 1 kHz beep starts at exactly the same instant.
    /// </summary>
    internal static void Sync(string output, int w, int h, int fps, int secs, bool fast = false)
    {
        string flash = string.Create(CultureInfo.InvariantCulture, $"between(mod(t\\,2)\\,1\\,{1 + 1.5 / fps:0.######})");
        Run("-f", "lavfi", "-i", $"testsrc2=s={w}x{h}:r={fps}:d={secs}",
            "-f", "lavfi", "-i", $"aevalsrc='0.7*sin(2*PI*1000*t)*between(mod(t,2),1,1.08)':s=48000:c=stereo:d={secs}",
            "-vf", $"drawbox=x=0:y=0:w=96:h=96:color=black:t=fill,drawbox=x=8:y=8:w=80:h=80:color=white:t=fill:enable='{flash}'",
            "-c:v", "libx264", "-preset", fast ? "ultrafast" : "veryfast", "-crf", "18", "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-b:a", "128k", output);
    }

    private static void Vfr(string output, string select)
    {
        string src = Get("hfr_1280x720_60.mp4");
        Run("-i", src, "-vf", $"select='{select}'", "-fps_mode", "passthrough",
            "-c:v", "libx264", "-preset", "veryfast", "-crf", "18", "-pix_fmt", "yuv420p", "-c:a", "copy", output);
    }

    private static void Odd(string output)
    {
        // A 1 px red frame around the picture shows whether any edge was cropped.
        // (testsrc2 rounds odd sizes down to even; testsrc keeps them. 4:4:4 because 4:2:0 can't be odd.)
        Run("-f", "lavfi", "-i", "testsrc=s=641x361:r=30:d=6",
            "-f", "lavfi", "-i", "sine=f=500:d=6:sample_rate=44100",
            "-vf", "format=yuv444p,drawbox=x=0:y=0:w=iw:h=ih:color=red:t=1",
            "-c:v", "ffv1", "-pix_fmt", "yuv444p", "-c:a", "flac", output);
    }

    private static void Rotated(string output)
    {
        string dir = WorkDir();
        string top = Path.Combine(dir, "top.txt");
        File.WriteAllText(top, "TOP OF PICTURE");
        string baseFile = Path.Combine(dir, "rot-base.mp4");
        Run("-f", "lavfi", "-i", "testsrc2=s=1280x720:r=30:d=8",
            "-f", "lavfi", "-i", "sine=f=700:d=8",
            "-vf", $"drawbox=x=0:y=0:w=1280:h=90:color=0x203060:t=fill,drawtext=fontfile={SansBold}:textfile={top}:expansion=none:fontsize=60:fontcolor=white:x=(w-tw)/2:y=14",
            "-c:v", "libx264", "-preset", "veryfast", "-crf", "18", "-pix_fmt", "yuv420p", "-c:a", "aac", baseFile);
        // Same pixels, plus a display-matrix rotation (how phones store portrait video).
        Run("-display_rotation", "90", "-i", baseFile, "-c", "copy", output);
    }

    // ------------------------------------------------------------------ helpers

    private static string Speech(string dir, int secs)
    {
        string f = Path.Combine(dir, "speech.txt");
        var sb = new StringBuilder();
        string para = "Today we look at how a binary search tree keeps its keys in order. " +
                      "Every node has a key. Smaller keys go to the left, larger keys go to the right. " +
                      "To insert a value we start at the root and walk down until we find an empty place. " +
                      "Notice that the shape of the tree depends on the order of insertion. ";
        while (sb.Length < secs * 16) sb.Append(para);
        File.WriteAllText(f, sb.ToString());
        return f;
    }

    private static string WorkDir()
    {
        string d = Path.Combine(Dir, "work", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(d);
        return d;
    }

    internal static void Run(params string[] args)
    {
        var psi = new ProcessStartInfo(Generator) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (string a in new[] { "-hide_banner", "-nostdin", "-y", "-loglevel", "error" }.Concat(args)) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        string err = p.StandardError.ReadToEnd();
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"Fixture generation failed ({p.ExitCode}): {err}\nargs: {string.Join(' ', args)}");
    }
}
