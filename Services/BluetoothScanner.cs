using System;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Radios;

namespace SignalLock.Services;

public interface IBluetoothScannerCallbacks
{
    void OnAvailabilityChanged(BluetoothAvailability availability);
    void OnDiscovered(DiscoveredDevice device);
}

/// <summary>
/// Wraps <see cref="BluetoothLEAdvertisementWatcher"/> with two scan modes:
/// discovery (any peripheral, populates the picker) and monitoring (filtered
/// by trusted Bluetooth address).
/// </summary>
public sealed class BluetoothScanner : IDisposable
{
    private readonly BluetoothLEAdvertisementWatcher _watcher = new()
    {
        ScanningMode = BluetoothLEScanningMode.Active,
    };

    private IBluetoothScannerCallbacks? _callbacks;
    private ulong? _trackedAddress;

    public BluetoothAvailability Availability { get; private set; } = BluetoothAvailability.Unknown;

    public bool IsScanning =>
        _watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started;

    public BluetoothScanner()
    {
        _watcher.Received += OnReceived;
        _watcher.Stopped += OnStopped;
    }

    public void SetCallbacks(IBluetoothScannerCallbacks callbacks)
    {
        _callbacks = callbacks;
    }

    public async Task RefreshAvailabilityAsync()
    {
        var newState = BluetoothAvailability.Unknown;
        try
        {
            var adapter = await BluetoothAdapter.GetDefaultAsync();
            if (adapter is null)
            {
                newState = BluetoothAvailability.Unsupported;
            }
            else if (!adapter.IsLowEnergySupported)
            {
                newState = BluetoothAvailability.Unsupported;
            }
            else
            {
                var radio = await adapter.GetRadioAsync();
                newState = radio?.State switch
                {
                    RadioState.On => BluetoothAvailability.Ready,
                    RadioState.Off => BluetoothAvailability.PoweredOff,
                    RadioState.Disabled => BluetoothAvailability.Unauthorized,
                    _ => BluetoothAvailability.Unknown,
                };
            }
        }
        catch (Exception ex)
        {
            Log.Bluetooth.Warn($"Availability query failed: {ex.Message}");
            newState = BluetoothAvailability.Unknown;
        }

        if (newState != Availability)
        {
            Availability = newState;
            Log.Bluetooth.Info($"Availability changed: {newState}");
            _callbacks?.OnAvailabilityChanged(newState);
        }
    }

    public async Task StartDiscoveryScanAsync()
    {
        _trackedAddress = null;
        await RefreshAvailabilityAsync();
        StartIfPossible();
    }

    public async Task StartMonitoringScanAsync(ulong address)
    {
        _trackedAddress = address;
        await RefreshAvailabilityAsync();
        StartIfPossible();
    }

    public void Stop()
    {
        try
        {
            if (_watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started)
            {
                _watcher.Stop();
            }
        }
        catch (Exception ex)
        {
            Log.Bluetooth.Warn($"Watcher stop failed: {ex.Message}");
        }
    }

    private void StartIfPossible()
    {
        if (Availability != BluetoothAvailability.Ready) return;
        try
        {
            if (_watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started)
            {
                _watcher.Stop();
            }
            _watcher.Start();
        }
        catch (Exception ex)
        {
            Log.Bluetooth.Error($"Watcher start failed: {ex.Message}");
        }
    }

    private void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        // 127 indicates RSSI is not available.
        int rssi = args.RawSignalStrengthInDBm;
        if (rssi == 127) return;

        if (_trackedAddress.HasValue && args.BluetoothAddress != _trackedAddress.Value) return;

        var name = args.Advertisement?.LocalName;
        if (string.IsNullOrEmpty(name)) name = "Unknown Device";

        var device = new DiscoveredDevice(args.BluetoothAddress, name, rssi, DateTime.UtcNow);
        _callbacks?.OnDiscovered(device);
    }

    private void OnStopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
    {
        if (args.Error != BluetoothError.Success)
        {
            Log.Bluetooth.Warn($"Watcher stopped with error: {args.Error}");
        }
    }

    public void Dispose()
    {
        try
        {
            _watcher.Received -= OnReceived;
            _watcher.Stopped -= OnStopped;
            Stop();
        }
        catch { /* swallow */ }
    }
}
