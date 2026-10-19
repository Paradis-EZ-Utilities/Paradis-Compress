using System.Text.Json;
using ParadisCompress.Core.Planning;

namespace ParadisCompress.Services;

/// <summary>The few choices remembered between launches: %LocalAppData%\ParadisCompress\settings.json.</summary>
public sealed class AppSettings
{
    public PresetId Preset { get; set; } = PresetId.ClassRecording;

    /// <summary>A folder the user explicitly chose last time; null = save next to the original.</summary>
    public string? OutputFolder { get; set; }

    public bool ShowAdvanced { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ParadisCompress", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Log.Warn("Settings could not be read; using defaults", ex);
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Log.Warn("Settings could not be saved", ex);
        }
    }
}
