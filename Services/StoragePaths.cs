using System;
using System.IO;

namespace SignalLock.Services;

public static class StoragePaths
{
    public static string AppDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SignalLock");

    public static string SettingsFile => Path.Combine(AppDataDir, "settings.json");
    public static string TrustedDeviceFile => Path.Combine(AppDataDir, "trusted-device.json");
}
