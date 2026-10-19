using System.Windows;
using System.Windows.Input;
using ParadisCompress.Core.Jobs;
using ParadisCompress.Services;

namespace ParadisCompress.UI;

public partial class AboutWindow : Window
{
    public AboutWindow(FfmpegTools? tools)
    {
        InitializeComponent();
        VersionText.Text = $"Version {AppInfo.Version} · {AppInfo.Tagline} · © 2026 D.Y. Paradis";
        FfmpegVersionText.Text = tools == null ? "FFmpeg was not found." : "Reading FFmpeg version…";
        if (tools != null) _ = LoadFfmpegVersionAsync(tools);
    }

    private async Task LoadFfmpegVersionAsync(FfmpegTools tools)
    {
        try
        {
            var r = await FfmpegProcess.RunAsync(tools.FfmpegPath, ["-hide_banner", "-version"], CancellationToken.None, captureStdout: true);
            string first = r.StandardOutput.Split('\n', 2)[0].Trim();
            FfmpegVersionText.Text = $"{first}\n{tools.FfmpegPath}";
        }
        catch (Exception ex)
        {
            Log.Warn("Could not read FFmpeg version", ex);
            FfmpegVersionText.Text = tools.FfmpegPath;
        }
    }

    private void OnCollection(object sender, MouseButtonEventArgs e)
    {
        if (!Dialogs.OpenUrl(AppInfo.CollectionUrl))
        {
            CollectionUrlText.Text = AppInfo.CollectionUrl + " (copied)";
            CollectionUrlText.Visibility = Visibility.Visible;
            try { Clipboard.SetText(AppInfo.CollectionUrl); } catch { }
        }
    }

    private void OnLicenses(object sender, MouseButtonEventArgs e)
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "licenses");
        Dialogs.RevealInExplorer(Directory.Exists(dir) ? dir : AppContext.BaseDirectory);
    }

    private void OnLogs(object sender, MouseButtonEventArgs e) => Dialogs.RevealInExplorer(Log.Directory);

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
