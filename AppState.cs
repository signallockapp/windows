using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using SignalLock.Services;
using Timer = System.Threading.Timer;

namespace SignalLock;

/// <summary>
/// Single runtime orchestrator. Mirrors macOS <c>AppState</c>:
/// owns the scanner, monitor, services and stores; raises a single
/// <see cref="StateChanged"/> event for the UI; marshals everything back
/// to the WinForms UI thread.
/// </summary>
public sealed class AppState : IBluetoothScannerCallbacks, IProximityCallbacks, IDisposable
{
    private readonly BluetoothScanner _scanner = new();
    private readonly ProximityMonitor _monitor;
    private readonly LockService _lockService = new();
    private readonly LoginItemService _loginItem = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly TrustedDeviceStore _trustedStore = new();

    private readonly object _gate = new();
    private readonly Dictionary<ulong, DiscoveredDevice> _discoveredById = new();
    private readonly SynchronizationContext _uiContext;
    private Timer? _discoveryRefreshTimer;
    private bool _isDiscovering;

    public AppSettings Settings { get; private set; }
    public TrustedDevice? TrustedDevice { get; private set; }
    public BluetoothAvailability Availability { get; private set; } = BluetoothAvailability.Unknown;
    public bool IsMonitoring { get; private set; }
    public bool IsAway { get; private set; }
    public DateTime? LastSeen { get; private set; }
    public int? CurrentRssi { get; private set; }
    public DateTime? LastTriggeredLockAt { get; private set; }
    public IReadOnlyList<DiscoveredDevice> DiscoveredDevices { get; private set; } = Array.Empty<DiscoveredDevice>();

    public event EventHandler? StateChanged;

    public AppState()
    {
        _uiContext = SynchronizationContext.Current ?? new SynchronizationContext();
        Settings = _settingsStore.Load();
        TrustedDevice = _trustedStore.Load();

        _monitor = new ProximityMonitor(Settings);
        _scanner.SetCallbacks(this);
        _monitor.SetCallbacks(this);

        SystemEvents.SessionSwitch += OnSessionSwitch;

        // Kick off availability query in background; result arrives via callback.
        _ = _scanner.RefreshAvailabilityAsync();
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionUnlock && IsMonitoring)
        {
            Log.App.Info("SessionUnlock → rearming proximity monitor");
            _monitor.Rearm();
            IsAway = false;
            CurrentRssi = null;
            LastSeen = null;
            RaiseStateChanged();
        }
    }

    // ----- Settings -----

    public void UpdateSettings(Action<AppSettings> mutate)
    {
        var next = Settings.Clone();
        mutate(next);
        Settings = next;
        _settingsStore.Save(next);
        _monitor.UpdateSettings(next);
        RaiseStateChanged();
    }

    public void SetStartAtLogin(bool enabled)
    {
        var success = _loginItem.SetEnabled(enabled);
        UpdateSettings(s => s.StartAtLogin = success && enabled);
    }

    // ----- Trusted device -----

    public void SelectTrustedDevice(DiscoveredDevice device)
    {
        var trusted = new TrustedDevice
        {
            Identifier = TrustedDevice.FormatAddress(device.Address),
            Name = device.Name,
        };
        _trustedStore.Save(trusted);
        TrustedDevice = trusted;
        Log.App.Info($"Trusted device selected: {trusted.Name} (id={trusted.Identifier})");
        if (IsMonitoring) StartMonitoring();
        RaiseStateChanged();
    }

    public void ClearTrustedDevice()
    {
        _trustedStore.Clear();
        TrustedDevice = null;
        if (IsMonitoring) StopMonitoring();
        RaiseStateChanged();
    }

    // ----- Discovery (for picker) -----

    public void StartDeviceDiscovery()
    {
        _isDiscovering = true;
        lock (_gate)
        {
            _discoveredById.Clear();
            DiscoveredDevices = Array.Empty<DiscoveredDevice>();
        }
        _ = _scanner.StartDiscoveryScanAsync();

        _discoveryRefreshTimer?.Dispose();
        _discoveryRefreshTimer = new Timer(_ => RefreshDiscoveryList(), null,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public void StopDeviceDiscovery()
    {
        _isDiscovering = false;
        _discoveryRefreshTimer?.Dispose();
        _discoveryRefreshTimer = null;

        if (!IsMonitoring)
        {
            _scanner.Stop();
        }
        else if (TrustedDevice is not null && TrustedDevice.TryParseAddress(out var address))
        {
            _ = _scanner.StartMonitoringScanAsync(address);
        }
    }

    private void RefreshDiscoveryList()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-15);
        DiscoveredDevice[] arr;
        lock (_gate)
        {
            arr = _discoveredById.Values
                .Where(d => d.LastSeen >= cutoff)
                .OrderByDescending(d => d.Rssi)
                .ToArray();
            DiscoveredDevices = arr;
        }
        RaiseStateChanged();
    }

    // ----- Monitoring -----

    public void StartMonitoring()
    {
        if (TrustedDevice is null) return;
        if (!TrustedDevice.TryParseAddress(out var address))
        {
            Log.App.Error($"Trusted device identifier '{TrustedDevice.Identifier}' is not a valid address.");
            return;
        }
        _monitor.Start();
        _ = _scanner.StartMonitoringScanAsync(address);
        IsMonitoring = true;
        UpdateSettings(s => s.MonitoringEnabled = true);
        Log.App.Info($"Monitoring started (device={TrustedDevice.Name}, threshold={Settings.RssiThreshold} dBm, awayDelay={Settings.AwayDelaySeconds}s)");
    }

    public void StopMonitoring()
    {
        _monitor.Stop();
        if (!_isDiscovering) _scanner.Stop();
        IsMonitoring = false;
        IsAway = false;
        CurrentRssi = null;
        UpdateSettings(s => s.MonitoringEnabled = false);
        Log.App.Info("Monitoring stopped");
    }

    public void TestLock()
    {
        _lockService.Lock();
    }

    // ----- IBluetoothScannerCallbacks -----

    public void OnAvailabilityChanged(BluetoothAvailability availability)
    {
        Availability = availability;
        if (availability != BluetoothAvailability.Ready && IsMonitoring)
        {
            CurrentRssi = null;
        }
        RaiseStateChanged();
    }

    public void OnDiscovered(DiscoveredDevice device)
    {
        lock (_gate) { _discoveredById[device.Address] = device; }

        if (IsMonitoring && TrustedDevice is not null
            && string.Equals(TrustedDevice.Identifier, device.Identifier, StringComparison.OrdinalIgnoreCase))
        {
            _monitor.Ingest(device.Rssi, device.LastSeen);
        }
    }

    // ----- IProximityCallbacks -----

    public void OnSmoothedRssi(int? rssi, DateTime? lastSeen)
    {
        CurrentRssi = rssi;
        LastSeen = lastSeen;
        RaiseStateChanged();
    }

    public void OnAwayChanged(bool away)
    {
        IsAway = away;
        RaiseStateChanged();
    }

    public void OnLockTrigger()
    {
        LastTriggeredLockAt = DateTime.UtcNow;
        _lockService.Lock();
        RaiseStateChanged();
    }

    // ----- UI dispatch -----

    private void RaiseStateChanged()
    {
        var handler = StateChanged;
        if (handler is null) return;
        _uiContext.Post(_ =>
        {
            try { handler(this, EventArgs.Empty); }
            catch (Exception ex) { Log.App.Warn($"StateChanged handler threw: {ex.Message}"); }
        }, null);
    }

    public void Dispose()
    {
        try { SystemEvents.SessionSwitch -= OnSessionSwitch; } catch { }
        _scanner.Dispose();
        _monitor.Dispose();
        _discoveryRefreshTimer?.Dispose();
    }
}
