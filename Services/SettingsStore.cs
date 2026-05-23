using System;
using System.IO;
using System.Text.Json;

namespace SignalLock.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(StoragePaths.SettingsFile)) return AppSettings.Default();
            var json = File.ReadAllText(StoragePaths.SettingsFile);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? AppSettings.Default();
        }
        catch (Exception ex)
        {
            Log.App.Warn($"Settings load failed; using defaults. {ex.Message}");
            return AppSettings.Default();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(StoragePaths.AppDataDir);
            File.WriteAllText(StoragePaths.SettingsFile, JsonSerializer.Serialize(settings, Json));
        }
        catch (Exception ex)
        {
            Log.App.Warn($"Settings save failed: {ex.Message}");
        }
    }
}
