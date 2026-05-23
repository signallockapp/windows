using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Timer = System.Threading.Timer;

namespace SignalLock.Services;

public interface IProximityCallbacks
{
    void OnSmoothedRssi(int? rssi, DateTime? lastSeen);
    void OnAwayChanged(bool away);
    void OnLockTrigger();
}

/// <summary>
/// Direct port of the macOS proximity logic. Maintains a moving-average RSSI
/// buffer and a last-seen timestamp; emits exactly one lock trigger per
/// "away episode". <see cref="Rearm"/> resets state so that screen unlocks
/// can start a fresh away cycle.
/// </summary>
public sealed class ProximityMonitor : IDisposable
{
    private readonly object _gate = new();
    private readonly Queue<int> _buffer = new();

    private AppSettings _settings;
    private DateTime? _lastSeen;
    private DateTime? _awaySince;
    private Timer? _timer;
    private IProximityCallbacks? _callbacks;

    public bool IsAway { get; private set; }
    public bool HasTriggeredLockForCurrentAway { get; private set; }

    /// <summary>
    /// One-way gate: <c>true</c> only after the trusted device has been
    /// observed near (smoothed RSSI ≥ threshold) since the last
    /// <see cref="Start"/>/<see cref="Rearm"/>. Prevents a re-lock loop when
    /// the user unlocks while the device is still out of range.
    /// </summary>
    private bool _hasConfirmedPresenceSinceArming;

    public ProximityMonitor(AppSettings settings)
    {
        _settings = settings.Clone();
    }

    public void SetCallbacks(IProximityCallbacks callbacks)
    {
        _callbacks = callbacks;
    }

    public void UpdateSettings(AppSettings settings)
    {
        lock (_gate)
        {
            _settings = settings.Clone();
            int window = Math.Max(1, _settings.RssiSmoothingWindow);
            while (_buffer.Count > window) _buffer.Dequeue();
        }
    }

    public void Start()
    {
        Stop();
        lock (_gate)
        {
            _buffer.Clear();
            _lastSeen = null;
            _awaySince = null;
            IsAway = false;
            HasTriggeredLockForCurrentAway = false;
            _hasConfirmedPresenceSinceArming = false;
        }
        _timer = new Timer(_ => Evaluate(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public void Stop()
    {
        var t = _timer;
        _timer = null;
        t?.Dispose();
    }

    /// <summary>Reset away-detection state without stopping the evaluation timer.</summary>
    public void Rearm()
    {
        bool wasAway;
        lock (_gate)
        {
            _buffer.Clear();
            _lastSeen = null;
            _awaySince = null;
            wasAway = IsAway;
            IsAway = false;
            HasTriggeredLockForCurrentAway = false;
            _hasConfirmedPresenceSinceArming = false;
        }
        _callbacks?.OnSmoothedRssi(null, null);
        if (wasAway) _callbacks?.OnAwayChanged(false);
    }

    public void Ingest(int rssi, DateTime atUtc)
    {
        int? smoothed;
        DateTime? lastSeen;
        bool justConfirmedPresence = false;
        lock (_gate)
        {
            _lastSeen = atUtc;
            _buffer.Enqueue(rssi);
            int window = Math.Max(1, _settings.RssiSmoothingWindow);
            while (_buffer.Count > window) _buffer.Dequeue();
            smoothed = _buffer.Count > 0 ? (int)Math.Round(_buffer.Average()) : (int?)null;
            lastSeen = _lastSeen;
            if (!_hasConfirmedPresenceSinceArming
                && smoothed.HasValue
                && smoothed.Value >= _settings.RssiThreshold)
            {
                _hasConfirmedPresenceSinceArming = true;
                justConfirmedPresence = true;
            }
        }
        if (justConfirmedPresence) Log.Proximity.Info($"Presence confirmed (smoothed={smoothed} dBm)");
        _callbacks?.OnSmoothedRssi(smoothed, lastSeen);
    }

    private void Evaluate()
    {
        var now = DateTime.UtcNow;

        bool awayBySignal;
        bool awayBySilence;
        int awayDelay;
        bool gated;

        lock (_gate)
        {
            // BLE advertising intervals are sub-second; if no packet has arrived
            // in 3s the smoothed RSSI is stale and would otherwise keep
            // awayBySignal from firing on sudden disconnects.
            if (_lastSeen.HasValue && (now - _lastSeen.Value).TotalSeconds > 3.0)
            {
                _buffer.Clear();
            }

            gated = !_hasConfirmedPresenceSinceArming;

            int? smoothed = _buffer.Count > 0 ? (int)Math.Round(_buffer.Average()) : (int?)null;
            awayBySignal = smoothed.HasValue && smoothed.Value < _settings.RssiThreshold;
            awayBySilence = _lastSeen.HasValue && (now - _lastSeen.Value).TotalSeconds > _settings.AwayDelaySeconds;
            awayDelay = _settings.AwayDelaySeconds;
        }

        // Loop guard: do not evaluate "away" until the device has been
        // confirmed near since arming. Stops re-locking after an unlock when
        // the device is still out of range.
        if (gated) return;

        bool away = awayBySignal || awayBySilence;

        if (away)
        {
            bool fireAwayChanged = false;
            bool fireLock = false;
            lock (_gate)
            {
                if (_awaySince is null)
                {
                    // Anchor silence-based away to lastSeen so elapsed reflects
                    // true absence duration — otherwise awayDelay is paid twice
                    // on sudden disconnects.
                    _awaySince = (awayBySilence && _lastSeen.HasValue) ? _lastSeen : now;
                    Log.Proximity.Info($"Away condition started (signal={awayBySignal}, silence={awayBySilence})");
                }
                var elapsed = (now - _awaySince!.Value).TotalSeconds;
                if (elapsed >= awayDelay)
                {
                    if (!IsAway)
                    {
                        IsAway = true;
                        Log.Proximity.Info($"Confirmed AWAY after {(int)elapsed}s; will trigger lock");
                        fireAwayChanged = true;
                    }
                    if (!HasTriggeredLockForCurrentAway)
                    {
                        HasTriggeredLockForCurrentAway = true;
                        fireLock = true;
                    }
                }
            }
            if (fireAwayChanged) _callbacks?.OnAwayChanged(true);
            if (fireLock) _callbacks?.OnLockTrigger();
        }
        else
        {
            bool wasAway = false;
            lock (_gate)
            {
                if (IsAway || _awaySince is not null)
                {
                    wasAway = true;
                    Log.Proximity.Info("Returned to NEAR; resetting away state");
                    IsAway = false;
                    _awaySince = null;
                    HasTriggeredLockForCurrentAway = false;
                }
            }
            if (wasAway) _callbacks?.OnAwayChanged(false);
        }
    }

    public void Dispose() => Stop();
}
