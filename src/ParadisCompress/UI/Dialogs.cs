using System.Diagnostics;
using System.Windows;
using ParadisCompress.Services;

namespace ParadisCompress.UI;

/// <summary>One place for the handful of message boxes the app shows.</summary>
public static class Dialogs
{
    public static void Error(Window? owner, string title, string message) =>
        Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public static void Info(Window? owner, string title, string message) =>
        Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    /// <summary>
    /// A question with clearly labelled answers ("Replace it", "Keep both", "Cancel") instead of Yes/No.
    /// Returns the index of the button pressed, or -1 if the window was closed.
    /// </summary>
    public static int Choose(Window? owner, string title, string message, params string[] buttons)
    {
        var dlg = new ChoiceDialog(title, message, buttons);
        if (owner != null && owner.IsVisible) dlg.Owner = owner;
        else dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dlg.ShowDialog();
        return dlg.Choice;
    }

    private static MessageBoxResult Show(Window? owner, string message, string title, MessageBoxButton buttons, MessageBoxImage icon)
    {
        string caption = $"Paradis Compress — {title}";
        return owner != null && owner.IsVisible
            ? MessageBox.Show(owner, message, caption, buttons, icon)
            : MessageBox.Show(message, caption, buttons, icon);
    }

    /// <summary>Opens a file with its default program (the finished video in the user's player).</summary>
    public static bool OpenWithDefaultApp(string path)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not open {path} with its default program", ex);
            return false;
        }
    }

    /// <summary>
    /// Opens a web address in the user's default browser. Returns false (and logs why) if Windows
    /// couldn't open it, so the caller can show the address instead.
    /// </summary>
    public static bool OpenUrl(string url) => OpenWithDefaultApp(url);

    /// <summary>Opens a folder in File Explorer, selecting the file when given one.</summary>
    public static void RevealInExplorer(string path)
    {
        try
        {
            bool isFile = File.Exists(path);
            var psi = new ProcessStartInfo("explorer.exe")
            {
                // Windows paths can't contain quotes, so quoting here is safe.
                Arguments = isFile ? $"/select,\"{path}\"" : $"\"{path}\"",
                UseShellExecute = true,
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not open {path} in Explorer", ex);
        }
    }
}
