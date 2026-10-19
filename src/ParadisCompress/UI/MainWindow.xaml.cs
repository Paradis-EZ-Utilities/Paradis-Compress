using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using ParadisCompress.Core.Jobs;
using ParadisCompress.Core.Media;
using ParadisCompress.Core.Planning;
using ParadisCompress.Core.Util;
using ParadisCompress.Services;

namespace ParadisCompress.UI;

/// <summary>
/// The whole app: choose a file, choose settings, compress. All decisions about encoding live in
/// ParadisCompress.Core; this class only reads the controls, shows results and asks questions.
/// </summary>
public partial class MainWindow : Window
{
    private const string VideoFilter =
        "Videos|*.mp4;*.m4v;*.mkv;*.mov;*.avi;*.webm;*.wmv;*.flv;*.ts;*.mts;*.m2ts;*.mpg;*.mpeg;*.3gp;*.ogv" +
        "|Audio|*.mp3;*.m4a;*.aac;*.wav;*.flac;*.ogg;*.opus;*.wma" +
        "|All files|*.*";

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly FfmpegTools? _tools = FfmpegTools.Locate();
    private readonly DispatcherTimer _elapsedTimer;

    private MediaInfo? _info;
    private string? _loadedPath;
    private CancellationTokenSource? _probeCts;
    private CancellationTokenSource? _jobCts;
    private Task? _jobTask;
    private bool _closeWhenJobEnds;
    private DateTime _jobStartedUtc;
    private JobProgress? _lastProgress;
    private JobResult? _lastResult;
    private JobRequest? _lastRequest;      // what produced _lastResult, for "Retry smaller"
    private Button? _popoverAnchor;         // the (i) or Details button whose popover is open
    private bool _statusIsJobOutcome;       // the status line reports the last job (cancelled, failed)

    private string? _chosenFolder;           // a folder the user picked; null = next to the original
    private bool _nameEditedByUser;
    private bool _settingName;
    private bool _ready;                     // false while the window is being built

    public MainWindow()
    {
        InitializeComponent();
        FitToWorkArea();
        VersionFooterText.Text = "v" + AppInfo.Version;

        FillCombo(PresetCombo,
            (nameof(PresetId.ClassRecording), Presets.ClassRecording.Name + " (recommended)"),
            (nameof(PresetId.Balanced), Presets.Balanced.Name),
            (nameof(PresetId.SmallestFile), Presets.SmallestFile.Name));
        PresetCombo.SelectedIndex = _settings.Preset switch
        {
            PresetId.Balanced => 1,
            PresetId.SmallestFile => 2,
            _ => 0,
        };

        FillCombo(ResolutionCombo, ("", "Preset default"), ("0", "Keep original"), ("1440", "1440p"), ("1080", "1080p"), ("720", "720p"), ("480", "480p"));
        FillCombo(FpsCombo, ("", "Preset default"), ("0", "Keep original"), ("60", "60 fps"), ("30", "30 fps"), ("24", "24 fps"), ("15", "15 fps"), ("10", "10 fps"));
        FillCombo(QualityCombo, ("", "Preset default"), ("18", "Highest (CRF 18)"), ("21", "High (CRF 21)"), ("23", "Good (CRF 23)"),
            ("26", "Medium (CRF 26)"), ("28", "Low (CRF 28)"), ("30", "Lowest (CRF 30)"));
        FillCombo(Mp3Combo, ("", "Automatic (64 kbit/s mono, 96 kbit/s stereo)"), ("64", "Smaller (64 kbit/s)"),
            ("96", "Standard (96 kbit/s)"), ("128", "Higher (128 kbit/s)"));

        if (_settings.OutputFolder is string f && Directory.Exists(f)) _chosenFolder = f;
        // Advanced settings always start collapsed (rc.2); the everyday workflow fits without them.

        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _elapsedTimer.Tick += (_, _) => UpdateProgressText();

        _ready = true;
        Refresh();
        Loaded += (_, _) =>
        {
            if (_tools == null)
            {
                Log.Error("ffmpeg/ffprobe not found next to the app or on PATH");
                SetStatus("FFmpeg is missing, so nothing can be compressed.", "Danger");
                Dialogs.Error(this, "FFmpeg is missing",
                    "Paradis Compress needs the \"ffmpeg\" folder that comes with it (ffmpeg.exe and ffprobe.exe). " +
                    "Unzip the whole download again and run ParadisCompress.exe from inside that folder.");
            }
            else
            {
                Log.Info($"Using {_tools.FfmpegPath}");
            }
        };
    }

    public void LoadFromCommandLine(string path)
    {
        // Opened via "Open with" or a file dropped on the exe. The path itself isn't logged (it's the user's).
        bool exists = File.Exists(path);
        Log.Info($"Started with a file argument (exists: {exists})");
        if (exists) _ = LoadFileAsync(path);
        else Dialogs.Error(this, "The video can't be found", "Paradis Compress was asked to open a file that isn't there. Choose the video again.");
    }

    // ------------------------------------------------------------------ choosing the input

    private void OnChooseFile(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Choose a video to compress", Filter = VideoFilter, CheckFileExists = true };
        if (_loadedPath != null) dlg.InitialDirectory = Path.GetDirectoryName(_loadedPath);
        if (dlg.ShowDialog(this) == true) _ = LoadFileAsync(dlg.FileName);
    }

    private static string[]? DroppedFiles(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) ? e.Data.GetData(DataFormats.FileDrop) as string[] : null;

    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        bool ok = _jobTask == null && DroppedFiles(e) is { Length: 1 } files && File.Exists(files[0]);
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        DropArea.BorderBrush = ok ? (Brush)FindResource("Accent") : (Brush)FindResource("Border");
        e.Handled = true;
    }

    private void OnPreviewDragLeave(object sender, DragEventArgs e) => DropArea.BorderBrush = (Brush)FindResource("Border");

    private void OnPreviewDrop(object sender, DragEventArgs e)
    {
        DropArea.BorderBrush = (Brush)FindResource("Border");
        e.Handled = true;
        if (_jobTask != null) return;
        var files = DroppedFiles(e);
        if (files == null || files.Length == 0) return;
        if (files.Length > 1)
        {
            SetStatus("Drop one video at a time.", "Warning");
            return;
        }
        if (Directory.Exists(files[0]))
        {
            SetStatus("That's a folder. Drop a video file instead.", "Warning");
            return;
        }
        _ = LoadFileAsync(files[0]);
    }

    private async Task LoadFileAsync(string path)
    {
        if (_jobTask != null || _tools == null) return;
        _probeCts?.Cancel();
        var cts = _probeCts = new CancellationTokenSource();

        _info = null;
        _loadedPath = path;
        _lastResult = null;
        _lastRequest = null;
        ClosePopover();
        ResultPanel.Visibility = Visibility.Collapsed;
        SetStatus(null);
        EmptyPanel.Visibility = Visibility.Collapsed;
        FilePanel.Visibility = Visibility.Visible;
        FileNameText.Text = Path.GetFileName(path);
        FileNameText.ToolTip = path;
        FileDetailText.ToolTip = null;
        FileDetailText.Text = "Reading video details…";
        FileNoteText.Visibility = Visibility.Collapsed;
        VideoModeRadio.IsEnabled = AudioModeRadio.IsEnabled = true;
        Refresh();

        try
        {
            var info = await Task.Run(() => MediaProber.ProbeAsync(_tools, path, cts.Token));
            if (cts.IsCancellationRequested) return;
            _info = info;
            Log.Info($"Loaded input: {info.FormatName}, {info.FileSizeBytes} bytes, {info.Duration?.TotalSeconds:0.###} s, " +
                     $"video {info.Video?.Codec} {info.Video?.CodedWidth}x{info.Video?.CodedHeight} rot {info.Video?.Rotation} " +
                     $"avg {info.Video?.AverageFps:0.###} fps (base {info.Video?.BaseFps:0.###}), audio {info.Audio?.Codec} {info.Audio?.Channels} ch {info.Audio?.SampleRate} Hz x{info.AudioStreamCount}");
            ShowFileDetails(info);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (CompressionException ex)
        {
            if (cts.IsCancellationRequested) return;
            Log.Warn($"Could not read input: {ex.Message} {ex.Detail}");
            FileDetailText.Text = ex.Title;
            FileNoteText.Text = ex.Message;
            FileNoteText.Foreground = (Brush)FindResource("Danger");
            FileNoteText.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            Log.Error("Probing failed unexpectedly", ex);
            FileDetailText.Text = "This file couldn't be read.";
        }

        _nameEditedByUser = false;
        Refresh();
    }

    private void ShowFileDetails(MediaInfo info)
    {
        var parts = new List<string> { Units.FormatDuration(info.Duration ?? TimeSpan.Zero) };
        if (info.Video is { } v)
        {
            parts.Add($"{v.DisplayWidth} × {v.DisplayHeight}");
            parts.Add(Units.FormatFps(v.Fps) + (v.LooksVariableFrameRate ? " (variable)" : ""));
        }
        else
        {
            parts.Add("sound only");
        }
        parts.Add(Units.FormatSize(info.FileSizeBytes));
        if (info.Video != null && info.Audio == null) parts.Add("no sound");
        FileDetailText.Text = string.Join("  ·  ", parts);
        FileDetailText.ToolTip = $"{Units.FormatBytes(info.FileSizeBytes)} · {info.FormatName}" +
            (info.Video != null ? $" · video {info.Video.Codec}" : "") + (info.Audio != null ? $" · audio {info.Audio.Codec}, {info.Audio.Channels} ch" : "");

        string? note = null;
        if (info.Video == null)
        {
            note = "No picture in this file, so only Extract audio is available.";
            VideoModeRadio.IsEnabled = false;
            AudioModeRadio.IsChecked = true;
        }
        else if (info.Audio == null)
        {
            note = "No sound in this video, so Extract audio isn't available.";
            AudioModeRadio.IsEnabled = false;
            VideoModeRadio.IsChecked = true;
        }
        FileNoteText.Foreground = (Brush)FindResource("Warning");
        FileNoteText.Text = note ?? "";
        FileNoteText.Visibility = note != null ? Visibility.Visible : Visibility.Collapsed;
    }

    // ------------------------------------------------------------------ options → plan preview

    private void OnOptionChanged(object sender, RoutedEventArgs e)
    {
        // "Cancelled" or "didn't finish" describes the last job; once settings change it's stale.
        if (_statusIsJobOutcome && _ready && _jobTask == null) SetStatus(null);
        Refresh();
    }

    private void OnNameChanged(object sender, TextChangedEventArgs e)
    {
        if (!_settingName) _nameEditedByUser = true;
        Refresh();
    }

    private bool AudioMode => AudioModeRadio.IsChecked == true;

    private PresetId SelectedPreset =>
        PresetCombo.SelectedItem is ComboBoxItem { Tag: string t } && Enum.TryParse(t, out PresetId id) ? id : PresetId.ClassRecording;

    private string CurrentFolder => _chosenFolder ?? (_loadedPath != null ? Path.GetDirectoryName(Path.GetFullPath(_loadedPath)) ?? "" : "");

    private string Extension => AudioMode ? ".mp3" : ".mp4";

    /// <summary>Reads the controls. Returns null and an explanation when something isn't filled in correctly.</summary>
    private CompressionOptions? ReadOptions(out string? problem)
    {
        problem = null;
        double? target = null;
        if (TargetCheck.IsChecked == true)
        {
            string t = TargetBox.Text.Trim();
            if (t.Length == 0)
            {
                problem = "Enter a target size in MB, or untick Target size.";
                return null;
            }
            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.CurrentCulture, out double mb)
                && !double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out mb))
            {
                problem = "Enter the target as a number of MB, for example 25 or 99.5.";
                return null;
            }
            if (mb <= 0 || mb > 1_000_000 || double.IsNaN(mb))
            {
                problem = "The target size should be more than 0 MB.";
                return null;
            }
            target = mb;
        }

        return new CompressionOptions
        {
            Mode = AudioMode ? OutputMode.AudioOnly : OutputMode.Video,
            Preset = SelectedPreset,
            TargetSizeMB = target,
            MaxShortSide = ComboInt(ResolutionCombo),
            MaxFps = ComboInt(FpsCombo),
            Crf = ComboInt(QualityCombo),
            MixToMono = MonoCheck.IsChecked == true,
            Mp3Kbps = ComboInt(Mp3Combo),
        };
    }

    /// <summary>Re-evaluates everything that depends on the current choices. Cheap: no I/O beyond a file-exists check.</summary>
    private void Refresh()
    {
        if (!_ready) return;
        bool running = _jobTask != null;
        bool audio = AudioMode;
        var preset = Presets.Get(SelectedPreset);

        // Audio-only hides the video presets and every video-only advanced control.
        PresetCombo.Visibility = audio ? Visibility.Collapsed : Visibility.Visible;
        AudioOnlyNote.Visibility = audio ? Visibility.Visible : Visibility.Collapsed;
        PresetLabel.Text = audio ? "Audio" : "Preset";
        AutomationProperties.SetName(PresetHelpButton, audio ? "Help: extract audio" : "Help: presets");
        PresetHelpButton.ToolTip = audio ? "About Extract audio" : "About the presets";
        foreach (var el in new UIElement[] { ResolutionLabel, ResolutionCombo, FpsLabel, FpsCombo, QualityLabel, QualityCombo })
            el.Visibility = audio ? Visibility.Collapsed : Visibility.Visible;
        Mp3Label.Visibility = Mp3Combo.Visibility = audio ? Visibility.Visible : Visibility.Collapsed;
        bool targeting = TargetCheck.IsChecked == true;
        TargetBox.IsEnabled = targeting;
        QualityCombo.IsEnabled = !targeting;
        SetDefaultLabel(ResolutionCombo, preset.MaxShortSide is int s ? $"Preset default ({s}p)" : "Preset default (keep original)");
        SetDefaultLabel(FpsCombo, $"Preset default ({preset.MaxFps:0} fps)");
        SetDefaultLabel(QualityCombo, $"Preset default (CRF {preset.Crf})");
        ExtensionText.Text = Extension;
        CompressButton.Content = audio ? "EXTRACT AUDIO" : "COMPRESS";
        TargetCheck.ToolTip = audio
            ? "Picks the MP3 bitrate so the file fits (approximate)"
            : "Two-pass encode aimed a little under this size (approximate)";

        // Folder and suggested name.
        FolderText.Text = _loadedPath == null && _chosenFolder == null
            ? "Next to the original video"
            : CurrentFolder + (_chosenFolder == null ? "  (same folder as the original)" : "");
        FolderText.ToolTip = CurrentFolder.Length > 0 ? CurrentFolder : null;
        if (_loadedPath != null && !_nameEditedByUser && CurrentFolder.Length > 0)
        {
            string suggestion = FileNaming.SuggestBaseName(_loadedPath, audio);
            string unique = FileNaming.MakeUnique(FileNaming.Combine(CurrentFolder, suggestion, Extension));
            SetName(Path.GetFileNameWithoutExtension(unique));
        }

        // Plan preview.
        string? problem = null;
        PlanNotesText.Visibility = Visibility.Collapsed;
        TargetMessageText.Visibility = Visibility.Collapsed;
        PlanSummaryText.Text = "";
        PlanSummaryLine.Text = "";
        PlanSummaryLine.ToolTip = null;
        var options = ReadOptions(out string? optionProblem);
        if (_info != null && options != null)
        {
            try
            {
                var plan = EncodePlanner.Plan(_info, options);
                PlanSummaryText.Text = DescribeForPeople(plan);
                PlanSummaryLine.Text = DescribeShort(plan);
                PlanSummaryLine.ToolTip = PlanSummaryText.Text + (plan.Notes.Count > 0 ? "\n" + string.Join("\n", plan.Notes) : "");
                if (plan.Notes.Count > 0)
                {
                    PlanNotesText.Text = string.Join("\n", plan.Notes.Select(n => "• " + n));
                    PlanNotesText.Visibility = Visibility.Visible;
                }
                if (plan.Target is { } t)
                {
                    // One concise line, always visible before starting; the full text is in the tooltip and dialogs.
                    ShowTargetMessage(t.ShortMessage.Length > 0 ? t.ShortMessage : t.Message, t.Verdict switch
                    {
                        TargetVerdict.Ok when !t.NotSmallerThanSource => "TextDim",
                        TargetVerdict.Ok or TargetVerdict.Low => "Warning",
                        _ => "Danger",
                    }, t.Message);
                    if (t.Verdict == TargetVerdict.Impossible) problem = t.Message;
                }
            }
            catch (CompressionException ex)
            {
                problem = ex.Message;
            }
        }
        else if (targeting && TargetBox.Text.Trim().Length == 0)
        {
            ShowTargetMessage("Enter a size in MB (1 MB = 1,000,000 bytes).", "TextDim", null);
        }
        else if (optionProblem != null && targeting)
        {
            ShowTargetMessage(optionProblem, "Warning", null);
        }

        bool nameOk = NameBox.Text.Trim().Length > 0;
        CompressButton.IsEnabled = !running && _tools != null && _info != null && options != null && problem == null && nameOk;
        if (!running && _info != null && _lastResult == null && StatusText.Visibility != Visibility.Visible && problem != null && !targeting)
            SetStatus(problem, "Warning");
    }

    private void ShowTargetMessage(string text, string brush, string? tooltip)
    {
        TargetMessageText.Text = text;
        TargetMessageText.Foreground = (Brush)FindResource(brush);
        TargetMessageText.ToolTip = tooltip != null && tooltip != text ? tooltip : null;
        TargetMessageText.Visibility = Visibility.Visible;
    }

    /// <summary>The status line under the main button: errors, cancellation, input problems. Null hides it.</summary>
    private void SetStatus(string? text, string brush = "TextDim", bool jobOutcome = false)
    {
        _statusIsJobOutcome = jobOutcome && !string.IsNullOrEmpty(text);
        if (!_statusIsJobOutcome) StatusText.ToolTip = null;
        StatusText.Text = text ?? "";
        StatusText.Foreground = (Brush)FindResource(brush);
        StatusText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnTargetCheckChanged(object sender, RoutedEventArgs e)
    {
        OnOptionChanged(sender, e);
        if (TargetCheck.IsChecked == true && _ready)
        {
            TargetBox.Focus();
            TargetBox.SelectAll();
        }
    }

    private static string DescribeForPeople(EncodePlan p)
    {
        var ci = CultureInfo.CurrentCulture;
        string ch(int c) => c == 1 ? "mono" : "stereo";
        if (p.Mode == OutputMode.AudioOnly)
            return string.Format(ci, "Will create: MP3, {0} kbit/s {1}{2}.", p.AudioKbps, ch(p.AudioChannels),
                p.AudioSampleRate is int r ? $", {r / 1000.0:0.#} kHz" : "");
        string rate = p.TwoPass
            ? string.Format(ci, "two-pass H.264 at about {0:N0} kbit/s", p.VideoBitsPerSecond / 1000.0)
            : string.Format(ci, "H.264 quality CRF {0}", p.Crf);
        string fps = p.OutputFps is double f ? Units.FormatFps(f) : "original frame rate";
        string audio = p.IncludeAudio ? string.Format(ci, "AAC {0} kbit/s {1}", p.AudioKbps, ch(p.AudioChannels)) : "no sound";
        return string.Format(ci, "Will create: MP4, {0} × {1}, {2}, {3} (x264 {4}), {5}.", p.Width, p.Height, fps, rate, p.Preset.X264Preset, audio);
    }

    /// <summary>"MP4 · 1920×1080 · 30 fps · CRF 23" — the one line next to the Advanced toggle.</summary>
    private static string DescribeShort(EncodePlan p)
    {
        var ci = CultureInfo.CurrentCulture;
        if (p.Mode == OutputMode.AudioOnly)
            return string.Format(ci, "MP3 · {0} kbit/s · {1}", p.AudioKbps, p.AudioChannels == 1 ? "mono" : "stereo");
        string fps = p.OutputFps is double f ? Units.FormatFps(f) : p.EffectiveFps > 0 ? Units.FormatFps(p.EffectiveFps) : "";
        string rate = p.TwoPass ? string.Format(ci, "{0:N0} kbit/s two-pass", p.VideoBitsPerSecond / 1000.0) : $"CRF {p.Crf}";
        return string.Format(ci, "MP4 · {0}×{1} · {2} · {3}", p.Width, p.Height, fps, rate);
    }

    private void SetName(string name)
    {
        if (NameBox.Text == name) return;
        _settingName = true;
        NameBox.Text = name;
        _settingName = false;
    }

    private static void FillCombo(ComboBox combo, params (string tag, string text)[] items)
    {
        foreach (var (tag, text) in items) combo.Items.Add(new ComboBoxItem { Content = text, Tag = tag });
        combo.SelectedIndex = 0;
    }

    private static void SetDefaultLabel(ComboBox combo, string text)
    {
        if (combo.Items.Count > 0 && combo.Items[0] is ComboBoxItem first && !Equals(first.Content, text))
        {
            first.Content = text;
            // Refresh the closed box's text when the default item is the selected one.
            if (combo.SelectedIndex == 0) { combo.SelectedIndex = -1; combo.SelectedIndex = 0; }
        }
    }

    private static int? ComboInt(ComboBox combo) =>
        combo.SelectedItem is ComboBoxItem { Tag: string t } && int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : null;

    private void OnToggleAdvanced(object sender, RoutedEventArgs e)
    {
        SetAdvancedVisible(AdvancedPanel.Visibility != Visibility.Visible);
        if (AdvancedPanel.Visibility == Visibility.Visible)
            Dispatcher.BeginInvoke(() => AdvancedPanel.BringIntoView(), DispatcherPriority.Loaded);
    }

    private void SetAdvancedVisible(bool visible)
    {
        AdvancedPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        AdvancedToggle.Content = visible ? "Hide advanced settings" : "Show advanced settings";
        AutomationProperties.SetName(AdvancedToggle, visible ? "Hide advanced settings" : "Show advanced settings");
    }

    // ------------------------------------------------------------------ window size

    /// <summary>
    /// The default 640 × 600 (logical pixels, so it grows with display scaling) is clamped to the work
    /// area, so a small or highly scaled screen gets a window that fits, with the settings scrolling.
    /// </summary>
    private void FitToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        if (area.Height > 0 && Height > area.Height - 16) Height = Math.Max(MinHeight, area.Height - 16);
        if (area.Width > 0 && Width > area.Width - 16) Width = Math.Max(MinWidth, area.Width - 16);
        if (area.Height > 0 && MinHeight > area.Height - 16) MinHeight = Math.Max(360, area.Height - 16);
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_popoverAnchor != null) Dispatcher.BeginInvoke(PositionPopover, DispatcherPriority.Loaded);
    }

    // ------------------------------------------------------------------ help popovers

    private void OnPresetHelp(object sender, RoutedEventArgs e)
    {
        if (AudioMode)
        {
            TogglePopover(PresetHelpButton, "Extract audio", body =>
            {
                Para(body, "Saves only the sound, as a constant-bitrate MP3 that plays almost anywhere: 96 kbit/s for stereo or 64 kbit/s for mono by default, plenty for speech.");
                Para(body, null, ("Slides, code and anything else shown on screen are not included", true), (", so use this when the spoken explanation is enough. No transcription is done.", false));
                Para(body, "The MP3 quality can be changed under Advanced settings. Video presets and picture settings don't apply here.");
            });
            return;
        }
        TogglePopover(PresetHelpButton, "Presets", body =>
        {
            foreach (var p in Presets.All)
            {
                string name = p.Name + (p.Id == PresetId.ClassRecording ? " (recommended)" : "");
                Para(body, null, (name + ". ", true), (p.Detail, false));
            }
            Para(body, "All presets make an MP4 (H.264 and AAC) that plays almost anywhere, and never enlarge the picture or raise the frame rate. Advanced settings can override them for one job.");
        });
    }

    private void OnTargetHelp(object sender, RoutedEventArgs e)
    {
        TogglePopover(TargetHelpButton, "Target size", body =>
        {
            Para(body, null, ("Approximate size targeting. ", true),
                ("The app aims a little under your target and checks the finished file's exact size in bytes before reporting success.", false));
            Para(body, null, ("Decimal units. ", true),
                ("1 MB = 1,000,000 bytes, so 500 MB means at most 500,000,000 bytes, the way upload limits are usually stated. " +
                 "File Explorer counts in binary units (1 MiB = 1,048,576 bytes) and shows the same file about 5% smaller.", false));
            if (AudioMode)
            {
                Para(body, null, ("Extract audio. ", true),
                    ("The target picks the MP3 bitrate (constant bitrate) so the file fits. It can only lower the bitrate, never raise it above the MP3 quality under Advanced. There is no second pass.", false));
            }
            else
            {
                Para(body, null, ("Two passes. ", true),
                    ("The video is analysed, then compressed at the bitrate that fits, so it takes longer. Sound and MP4 overhead are budgeted first, with about 4% headroom for encoder variation.", false));
                Para(body, null, ("Still too big? ", true),
                    ("The second pass is repeated at a lower bitrate, up to three times. If the file is still over, the result says so and offers Retry smaller.", false));
            }
            Para(body, null, ("No guarantee. ", true),
                ("A website can still refuse a file for its length, resolution or format, or because it counts sizes differently.", false));
        });
    }

    /// <summary>Adds a paragraph: plain text, or runs of (text, bold).</summary>
    private static void Para(TextBlock body, string? text, params (string text, bool bold)[] runs)
    {
        if (body.Inlines.Count > 0) { body.Inlines.Add(new LineBreak()); body.Inlines.Add(new LineBreak()); }
        if (text != null) body.Inlines.Add(new Run(text));
        foreach (var (t, bold) in runs) body.Inlines.Add(bold ? new Bold(new Run(t)) : new Run(t));
    }

    /// <summary>Opens the popover for <paramref name="anchor"/>, or closes it if that one is already open.</summary>
    private void TogglePopover(Button anchor, string title, Action<TextBlock> fill)
    {
        if (_popoverAnchor == anchor)
        {
            ClosePopover(returnFocus: true);
            return;
        }
        ClosePopover();
        PopoverTitle.Text = title;
        PopoverBody.Inlines.Clear();
        fill(PopoverBody);
        AutomationProperties.SetName(Popover, title);
        _popoverAnchor = anchor;
        anchor.Tag = "open";
        Popover.Visibility = Visibility.Visible;
        PopoverScroll.ScrollToTop();
        PositionPopover();
        // Keyboard and screen-reader users land in the popover; Esc or Close returns them to the button.
        Dispatcher.BeginInvoke(() =>
        {
            PositionPopover();
            PopoverScroll.Focus();
            UIElementAutomationPeer.CreatePeerForElement(Popover)?.RaiseAutomationEvent(AutomationEvents.MenuOpened);
        }, DispatcherPriority.Loaded);
    }

    /// <summary>Places the popover under (or above) its button, clamped so it never leaves the window.</summary>
    private void PositionPopover()
    {
        if (_popoverAnchor is not { } anchor || Popover.Visibility != Visibility.Visible || !anchor.IsVisible) return;
        const double margin = 10;
        double rootW = RootGrid.ActualWidth, rootH = RootGrid.ActualHeight;
        if (rootW <= 0 || rootH <= 0) return;
        double width = Math.Max(200, Math.Min(400, rootW - 2 * margin));
        Popover.Width = width;
        Popover.MaxHeight = Math.Max(120, rootH - 2 * margin);
        Popover.Measure(new Size(width, Popover.MaxHeight));
        double h = Math.Min(Popover.DesiredSize.Height, Popover.MaxHeight);

        Point a = anchor.TranslatePoint(new Point(0, 0), RootGrid);
        double left = Math.Clamp(a.X + anchor.ActualWidth / 2 - width / 2, margin, Math.Max(margin, rootW - width - margin));
        double below = a.Y + anchor.ActualHeight + 6;
        double above = a.Y - 6 - h;
        double top = below + h <= rootH - margin ? below
            : above >= margin ? above
            : Math.Max(margin, rootH - margin - h);
        Canvas.SetLeft(Popover, left);
        Canvas.SetTop(Popover, top);
    }

    private void ClosePopover(bool returnFocus = false)
    {
        if (_popoverAnchor is not { } anchor) return;
        _popoverAnchor = null;
        anchor.Tag = null;
        Popover.Visibility = Visibility.Collapsed;
        if (returnFocus && anchor.IsVisible) anchor.Focus();
    }

    private void OnPopoverClose(object sender, RoutedEventArgs e) => ClosePopover(returnFocus: true);

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _popoverAnchor != null)
        {
            ClosePopover(returnFocus: true);
            e.Handled = true;
        }
    }

    private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // A click anywhere outside the popover closes it (a click on its own button toggles it in the Click handler).
        if (_popoverAnchor == null || e.OriginalSource is not DependencyObject d) return;
        if (IsInside(d, Popover) || IsInside(d, _popoverAnchor)) return;
        ClosePopover();
    }

    private static bool IsInside(DependencyObject d, DependencyObject container)
    {
        for (var cur = d; cur != null; cur = cur is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(cur) : LogicalTreeHelper.GetParent(cur))
            if (cur == container) return true;
        return false;
    }

    // ------------------------------------------------------------------ output folder

    private void OnChangeFolder(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Choose where to save the new file" };
        if (CurrentFolder.Length > 0 && Directory.Exists(CurrentFolder)) dlg.InitialDirectory = CurrentFolder;
        if (dlg.ShowDialog(this) != true) return;
        _chosenFolder = dlg.FolderName;
        _settings.OutputFolder = _chosenFolder;
        _settings.Save();
        Refresh();
    }

    // ------------------------------------------------------------------ running a job

    private async void OnCompress(object sender, RoutedEventArgs e)
    {
        if (_info == null || _tools == null || _jobTask != null) return;
        ClosePopover();
        var options = ReadOptions(out string? problem);
        if (options == null)
        {
            Dialogs.Error(this, "Check the settings", problem ?? "Some settings aren't filled in.");
            return;
        }
        string folder = CurrentFolder;
        if (folder.Length == 0 || !Directory.Exists(folder))
        {
            Dialogs.Error(this, "Choose a folder", "The folder to save in doesn't exist any more. Choose another one with Folder….");
            return;
        }
        string output = FileNaming.Combine(folder, NameBox.Text, Extension);

        Preflight pre;
        try
        {
            pre = CompressionJob.Check(new JobRequest(_info, options, output, false));
        }
        catch (CompressionException ex)
        {
            Dialogs.Error(this, ex.Title, ex.Message);
            return;
        }

        if (!ConfirmTarget(pre.Plan)) return;

        bool replace = false;
        if (pre.OutputExists)
        {
            string unique = FileNaming.MakeUnique(output);
            int c = Dialogs.Choose(this, "A file with this name already exists",
                $"\"{Path.GetFileName(output)}\" is already in this folder.\n\n" +
                $"Keep both saves the new file as \"{Path.GetFileName(unique)}\".\n" +
                "Replace it swaps the existing file for the new one, but only once the new one is finished and checked.\n\n" +
                "Your original video is never changed.",
                "Keep both", "Replace it", "Cancel");
            if (c == 0) output = unique;
            else if (c == 1) replace = true;
            else return;
        }

        if (!ConfirmDiskSpace(pre)) return;

        _settings.Preset = SelectedPreset;
        _settings.Save();
        await RunJobAsync(new JobRequest(_info, options, output, replace));
    }

    /// <summary>Impossible targets are refused; very low ones need a confirmation. True = go ahead.</summary>
    private bool ConfirmTarget(EncodePlan plan)
    {
        if (plan.Target is { Verdict: TargetVerdict.Impossible } imp)
        {
            Dialogs.Error(this, "Target size too small", imp.Message);
            return false;
        }
        return plan.Target is not { Verdict: TargetVerdict.TooLow } low ||
               Dialogs.Choose(this, "That target is very small", low.Message, "Change settings", "Start anyway") == 1;
    }

    private bool ConfirmDiskSpace(Preflight pre) =>
        !(pre.LowDiskSpace && pre.FreeBytes is long free) ||
        Dialogs.Choose(this, "Disk space is low",
            $"The destination drive has {Units.FormatSize(free)} free, and the new file could need up to about " +
            $"{Units.FormatSize(pre.Plan.EstimatedBytes)}. If the drive fills up, compression stops and nothing is saved.",
            "Cancel", "Continue anyway") == 1;

    /// <summary>
    /// "Retry smaller": the same job at a lower bitrate, written over the over-target file this window just
    /// made (only once the new file is finished and checked, like any replace).
    /// </summary>
    private async void OnRetrySmaller(object sender, RoutedEventArgs e)
    {
        if (_jobTask != null || _lastResult is not { } last || _lastRequest is not { } req) return;
        ClosePopover();
        var retry = last.RetryOptions(req.Options);
        if (retry == null)
        {
            Dialogs.Info(this, "Can't go lower", "There's no lower bitrate left to try. Try a larger target, the Smallest File preset, or a lower resolution under Advanced settings.");
            return;
        }
        var request = req with { Options = retry, OutputPath = last.OutputPath, ReplaceExisting = File.Exists(last.OutputPath) };
        Preflight pre;
        try
        {
            pre = CompressionJob.Check(request);
        }
        catch (CompressionException ex)
        {
            Dialogs.Error(this, ex.Title, ex.Message);
            return;
        }
        if (!ConfirmTarget(pre.Plan) || !ConfirmDiskSpace(pre)) return;
        Log.Info("Retry smaller requested after an over-target result");
        await RunJobAsync(request);
    }

    private async Task RunJobAsync(JobRequest request)
    {
        var cts = _jobCts = new CancellationTokenSource();
        _lastResult = null;
        _lastRequest = null;
        _lastProgress = null;
        _jobStartedUtc = DateTime.UtcNow;
        SetRunning(true);

        var progress = new Progress<JobProgress>(p =>
        {
            _lastProgress = p;
            UpdateProgressText();
        });
        var task = Task.Run(() => CompressionJob.RunAsync(_tools!, request, progress, cts.Token));
        _jobTask = task;
        _elapsedTimer.Start();
        (string Title, string Message)? error = null;
        try
        {
            var result = await task;
            _lastResult = result;
            _lastRequest = request;
            SetStatus(null);
            ShowResult(result);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Cancelled. Nothing was saved, and your original is untouched.", jobOutcome: true);
        }
        catch (CompressionException ex)
        {
            SetStatus(ex.Title + ". Nothing was saved, and your original is untouched.", "Danger", jobOutcome: true);
            StatusText.ToolTip = ex.Message;
            error = (ex.Title, ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error("Job failed unexpectedly", ex);
            SetStatus("Something went wrong. Nothing was saved, and your original is untouched. See the log folder.", "Danger", jobOutcome: true);
            error = ("Something went wrong", $"The log has the details:\n{Log.Directory}");
        }
        finally
        {
            _elapsedTimer.Stop();
            _jobTask = null;
            _jobCts = null;
            cts.Dispose();
            SetRunning(false);
            _nameEditedByUser = false;   // suggest a fresh, unused name for the next job
            Refresh();
            if (_lastResult == null) CompressButton.Focus();
        }

        if (_closeWhenJobEnds) Close();
        // Shown after the window is back to its idle state, so the dialog doesn't sit over a stale progress bar.
        else if (error is var (title, message)) Dialogs.Error(this, title, message);
    }

    private void SetRunning(bool running)
    {
        SettingsPanel.IsEnabled = !running;
        CompressButton.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        ProgressPanel.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.IsEnabled = running;
        if (running)
        {
            ClosePopover();
            ResultPanel.Visibility = Visibility.Collapsed;
            SetStatus(null);
            StatusText.ToolTip = null;
            JobProgressBar.Value = 0;
            ProgressStageText.Text = "Getting ready…";
            ProgressDetailText.Text = "";
            CancelButton.Focus();
        }
    }

    private void UpdateProgressText()
    {
        var p = _lastProgress;
        TimeSpan elapsed = DateTime.UtcNow - _jobStartedUtc;
        if (_jobCts?.IsCancellationRequested == true)
        {
            ProgressStageText.Text = "Cancelling…";
            return;
        }
        if (p == null)
        {
            ProgressDetailText.Text = $"{Units.FormatDuration(elapsed)} elapsed";
            return;
        }
        bool audio = AudioMode;
        ProgressStageText.Text = p.Stage switch
        {
            JobStage.Preparing => "Getting ready…",
            JobStage.Analysing => "Pass 1 of 2: analysing the video",
            JobStage.Encoding when p.PassCount == 2 => "Pass 2 of 2: compressing",
            JobStage.Refitting => $"Pass {p.Pass} of {p.PassCount}: fitting the target size",
            JobStage.Encoding => audio ? "Extracting audio" : "Compressing",
            JobStage.Checking => "Checking the new file…",
            JobStage.Saving => "Saving…",
            _ => "Working…",
        };
        JobProgressBar.Value = p.Fraction;
        string remaining = p.Stage is JobStage.Checking or JobStage.Saving ? "almost done"
            : p.Remaining is TimeSpan r ? Units.FormatRemaining(r) + " left (estimate)"
            : "estimating time left…";
        ProgressDetailText.Text = $"{p.Fraction * 100:0}%  ·  {Units.FormatDuration(elapsed)} elapsed  ·  {remaining}";
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        if (_jobCts == null) return;
        Log.Info("Cancel requested by user");
        CancelButton.IsEnabled = false;
        _jobCts.Cancel();
        UpdateProgressText();
    }

    /// <summary>
    /// The compact completion area: one headline (size change, or a clear over-target warning), one line
    /// with the elapsed time and target check, and the buttons. Everything else is behind Details.
    /// </summary>
    private void ShowResult(JobResult r)
    {
        // Floor, so 99.6 % smaller reads "99%" rather than an impossible "100%".
        double pct = r.DidNotShrink ? Math.Ceiling(Math.Abs(r.Reduction * 100)) : Math.Floor(r.Reduction * 100);
        string sizes = $"{Units.FormatSize(r.SourceBytes)} → {Units.FormatSizePrecise(r.OutputBytes)}";
        string took = $"Took {Units.FormatDuration(r.Elapsed)}";
        bool retryable = r.AboveTarget && _lastRequest is { } req && r.RetryOptions(req.Options) != null;
        RetryButton.Visibility = retryable ? Visibility.Visible : Visibility.Collapsed;

        if (r.AboveTarget && r.Plan.Target is { } over)
        {
            // Never reported as success: the actual bytes are over the requested size.
            long excess = r.OutputBytes - over.TargetBytes;
            ResultHeadline.Text = $"Not within your {Units.FormatTargetBytes(over.TargetBytes)} target: over by " +
                (excess < Units.BytesPerMB ? Units.FormatBytes(excess) : Units.FormatSizePrecise(excess));
            ResultHeadline.Foreground = (Brush)FindResource("Danger");
            ResultSubline.Text = $"The file is {Units.FormatBytes(r.OutputBytes)}. " +
                (retryable ? "Retry smaller compresses again at a lower bitrate." : "Try a smaller target or a lower resolution.") + $" {took}.";
        }
        else if (r.DidNotShrink)
        {
            ResultHeadline.Text = $"Done, but not smaller: {sizes}" + (pct >= 0.5 ? $" ({pct:0}% larger)" : "");
            ResultHeadline.Foreground = (Brush)FindResource("Warning");
            ResultSubline.Text = $"The original was probably already compressed efficiently; keep whichever you prefer. {took}.";
        }
        else
        {
            ResultHeadline.Text = $"Done: {sizes} ({pct:0}% smaller)";
            ResultHeadline.Foreground = (Brush)FindResource("Success");
            ResultSubline.Text = r.Plan.Target is { } t
                ? $"Within the {Units.FormatTargetBytes(t.TargetBytes)} target ({Units.FormatBytes(r.OutputBytes)}). {took}."
                : $"{took}. Saved as {Path.GetFileName(r.OutputPath)}.";
        }
        ResultPanel.Visibility = Visibility.Visible;
        AutomationProperties.SetName(ResultPanel, ResultHeadline.Text);
        // Keyboard focus moves from the vanished Cancel button to the result's first action.
        Dispatcher.BeginInvoke(() => (retryable ? RetryButton : OpenFileButton).Focus(), DispatcherPriority.Loaded);
        if (UIElementAutomationPeer.CreatePeerForElement(ResultHeadline) is { } peer)
            peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private void OnResultDetails(object sender, RoutedEventArgs e)
    {
        if (_lastResult is not { } r) return;
        TogglePopover(DetailsButton, "Result details", body =>
        {
            Para(body, null, ("Saved as: ", true), (r.OutputPath, false));
            Para(body, null, ("Original: ", true), ($"{Units.FormatSize(r.SourceBytes)} ({Units.FormatBytes(r.SourceBytes)})", false));
            body.Inlines.Add(new LineBreak());
            body.Inlines.Add(new Bold(new Run("New file: ")));
            body.Inlines.Add(new Run($"{Units.FormatSizePrecise(r.OutputBytes)} ({Units.FormatBytes(r.OutputBytes)}), " +
                (r.DidNotShrink ? $"{Math.Abs(r.Reduction * 100):0.#}% larger" : $"{r.Reduction * 100:0.#}% smaller")));
            if (r.Plan.Target is { } t)
            {
                body.Inlines.Add(new LineBreak());
                body.Inlines.Add(new Bold(new Run("Target: ")));
                body.Inlines.Add(new Run($"{Units.FormatTargetBytes(t.TargetBytes)} = {Units.FormatBytes(t.TargetBytes)}: " +
                    (r.AboveTarget ? $"ABOVE by {Units.FormatBytes(r.OutputBytes - t.TargetBytes)}" : $"within, {r.OutputBytes * 100.0 / t.TargetBytes:0.0}% of the target")));
            }
            Para(body, DescribeForPeople(r.Plan).Replace("Will create:", "Created:"));
            if (r.Plan.Notes.Count > 0)
                body.Inlines.Add(new Run("\n" + string.Join("\n", r.Plan.Notes.Select(n => "• " + n))));
            if (r.Plan.Mode == OutputMode.AudioOnly)
                Para(body, "This file has only the sound: no slides or on-screen content.");
            if (r.DidNotShrink)
                Para(body, "The new file is not smaller than the original, which was probably already compressed efficiently. Nothing was deleted; keep whichever you prefer.");
            if (r.AboveTarget)
                Para(body, "The file was kept, but it is larger than the size you asked for, so an upload with that limit would probably be refused. Retry smaller replaces it once the new file is finished and checked.");
            Para(body, $"Took {Units.FormatDuration(r.Elapsed)}. Your original video was not changed. Sizes use decimal units (1 MB = 1,000,000 bytes); Explorer shows binary units, so its numbers are about 5% smaller.");
        });
    }

    private void OnOpenResult(object sender, RoutedEventArgs e)
    {
        if (_lastResult == null) return;
        if (!File.Exists(_lastResult.OutputPath))
            Dialogs.Error(this, "The file isn't there", "The new file has been moved or deleted since it was saved.");
        else if (!Dialogs.OpenWithDefaultApp(_lastResult.OutputPath))
            Dialogs.Error(this, "Couldn't open the file", "Windows couldn't open the file. Try opening it from its folder.");
    }

    private void OnRevealResult(object sender, RoutedEventArgs e)
    {
        if (_lastResult != null) Dialogs.RevealInExplorer(_lastResult.OutputPath);
    }

    // ------------------------------------------------------------------ footer and closing

    private void OnAbout(object sender, RoutedEventArgs e) => new AboutWindow(_tools) { Owner = this }.ShowDialog();

    private void OnOpenLogs(object sender, RoutedEventArgs e) => Dialogs.RevealInExplorer(Log.Directory);

    private void OnMoreLink(object sender, RoutedEventArgs e)
    {
        if (!Dialogs.OpenUrl(AppInfo.CollectionUrl))
        {
            try { Clipboard.SetText(AppInfo.CollectionUrl); } catch { }
            Dialogs.Info(this, "More from Paradis EZ Utilities", $"Couldn't open the browser. The address was copied:\n{AppInfo.CollectionUrl}");
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_jobTask == null) return;
        e.Cancel = true;
        if (_closeWhenJobEnds) return; // already stopping
        if (Dialogs.Choose(this, "Compression is still running",
                "Stop it and close Paradis Compress? Nothing will be saved; your original is untouched.",
                "Keep compressing", "Stop and close") != 1)
            return;
        _closeWhenJobEnds = true;
        _jobCts?.Cancel();
    }
}
