using System.IO;
using System.Text.Json;

namespace FolderCompare.App;

public sealed class FolderCompareSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FolderCompareTool",
        "settings.json");

    public async Task<FolderCompareSettings> LoadAsync()
    {
        if (!File.Exists(SettingsPath))
        {
            return new FolderCompareSettings();
        }

        try
        {
            var json = await File.ReadAllTextAsync(SettingsPath);
            return JsonSerializer.Deserialize<FolderCompareSettings>(json, JsonOptions) ?? new FolderCompareSettings();
        }
        catch
        {
            return new FolderCompareSettings();
        }
    }

    public async Task SaveAsync(FolderCompareSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath) ?? throw new InvalidOperationException("Settings folder could not be resolved."));
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await File.WriteAllTextAsync(SettingsPath, json);
    }
}
