using System;
using System.IO;
using System.Text.Json;

namespace SignalLock.Services;

public sealed class TrustedDeviceStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public TrustedDevice? Load()
    {
        try
        {
            if (!File.Exists(StoragePaths.TrustedDeviceFile)) return null;
            var json = File.ReadAllText(StoragePaths.TrustedDeviceFile);
            return JsonSerializer.Deserialize<TrustedDevice>(json);
        }
        catch (Exception ex)
        {
            Log.App.Warn($"Trusted device load failed: {ex.Message}");
            return null;
        }
    }

    public void Save(TrustedDevice device)
    {
        try
        {
            Directory.CreateDirectory(StoragePaths.AppDataDir);
            File.WriteAllText(StoragePaths.TrustedDeviceFile, JsonSerializer.Serialize(device, Json));
        }
        catch (Exception ex)
        {
            Log.App.Warn($"Trusted device save failed: {ex.Message}");
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(StoragePaths.TrustedDeviceFile)) File.Delete(StoragePaths.TrustedDeviceFile);
        }
        catch (Exception ex)
        {
            Log.App.Warn($"Trusted device clear failed: {ex.Message}");
        }
    }
}
