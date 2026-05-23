using System;
using System.Globalization;

namespace SignalLock;

public enum BluetoothAvailability
{
    Unknown,
    Unsupported,
    Unauthorized,
    PoweredOff,
    Ready,
}

public sealed class AppSettings
{
    public int RssiThreshold { get; set; } = -80;
    public int AwayDelaySeconds { get; set; } = 20;
    public int RssiSmoothingWindow { get; set; } = 5;
    public bool MonitoringEnabled { get; set; } = false;
    public bool StartAtLogin { get; set; } = false;

    public AppSettings Clone() => new()
    {
        RssiThreshold = RssiThreshold,
        AwayDelaySeconds = AwayDelaySeconds,
        RssiSmoothingWindow = RssiSmoothingWindow,
        MonitoringEnabled = MonitoringEnabled,
        StartAtLogin = StartAtLogin,
    };

    public static AppSettings Default() => new();
}

public sealed class TrustedDevice
{
    public string Identifier { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// On Windows, the BLE peripheral identifier is the 48-bit Bluetooth address packed
    /// into a ulong. We store it as a 12-character upper-case hex string for portability.
    /// </summary>
    public bool TryParseAddress(out ulong address) =>
        ulong.TryParse(Identifier, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address);

    public static string FormatAddress(ulong address) =>
        address.ToString("X12", CultureInfo.InvariantCulture);
}

public sealed class DiscoveredDevice
{
    public ulong Address { get; }
    public string Name { get; }
    public int Rssi { get; }
    public DateTime LastSeen { get; }

    public DiscoveredDevice(ulong address, string name, int rssi, DateTime lastSeen)
    {
        Address = address;
        Name = name;
        Rssi = rssi;
        LastSeen = lastSeen;
    }

    public string Identifier => TrustedDevice.FormatAddress(Address);
}
